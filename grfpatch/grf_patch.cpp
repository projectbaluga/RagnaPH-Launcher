#include "grf_patch.hpp"
#include <Windows.h>
#include <algorithm>
#include <filesystem>
#include <unordered_map>
#include <memory>
#include <cstdio>
#include <zlib.h>

#pragma comment(lib, "Cabinet.lib")  // for compression API

// -----------------------------------------------------------------------------
// Zlib compression wrapper using Windows Compression API (zlib format)
// -----------------------------------------------------------------------------
namespace {

class Compressor {
public:
  Compressor() {
    CreateCompressor(COMPRESSION_FORMAT_ZLIB, nullptr, &handle_);
  }
  ~Compressor() { if (handle_) CloseCompressor(handle_); }
  bool Compress(const uint8_t* src, size_t srcSize, std::vector<uint8_t>& out) {
    if (!handle_) return false;
    size_t needed = 0;
    if (!Compress(handle_, src, srcSize, nullptr, 0, &needed)) return false;
    out.resize(needed);
    return !!Compress(handle_, src, srcSize, out.data(), needed, &needed);
  }
private:
  COMPRESSOR_HANDLE handle_{};
};

class Decompressor {
public:
  Decompressor() { CreateDecompressor(COMPRESSION_FORMAT_ZLIB, nullptr, &handle_); }
  ~Decompressor() { if (handle_) CloseDecompressor(handle_); }
  bool Decompress(const uint8_t* src, size_t srcSize, std::vector<uint8_t>& out, size_t expected) {
    if (!handle_) return false;
    out.resize(expected);
    size_t actual = expected;
    return !!Decompress(handle_, src, srcSize, out.data(), expected, &actual);
  }
private:
  DECOMPRESSOR_HANDLE handle_{};
};

std::wstring ToLower(const std::wstring& s) {
  std::wstring r = s;
  std::transform(r.begin(), r.end(), r.begin(), ::towlower);
  return r;
}

bool NormalizePath(const std::wstring& in, std::wstring& out) {
  if (in.empty()) return false;
  std::wstring tmp = in;
  for (auto& c : tmp) if (c == L'/') c = L'\\';
  if (tmp.find(L"..") != std::wstring::npos) return false;
  out = tmp;
  return true;
}

struct FileEntry {
  std::wstring pathOriginal;
  std::vector<uint8_t> data;
};

class GrfFile {
public:
  explicit GrfFile(const std::wstring& path) : path_(path) {}
  bool Load() {
    HANDLE h = CreateFileW(path_.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{};
    GetFileSizeEx(h, &size);
    std::vector<uint8_t> buf(size.QuadPart);
    DWORD read = 0;
    ReadFile(h, buf.data(), (DWORD)buf.size(), &read, nullptr);
    CloseHandle(h);
    if (buf.size() < 16) return false;
    uint32_t count = *(uint32_t*)&buf[12];
    size_t pos = 16;
    for (uint32_t i = 0; i < count; ++i) {
      uint32_t len = *(uint32_t*)&buf[pos]; pos += 4;
      std::wstring path((wchar_t*)&buf[pos], len); pos += len * 2;
      uint32_t dlen = *(uint32_t*)&buf[pos]; pos += 4;
      FileEntry fe;
      fe.pathOriginal = path;
      fe.data.assign(buf.begin()+pos, buf.begin()+pos+dlen);
      pos += dlen;
      entries_[ToLower(path)] = std::move(fe);
    }
    return true;
  }

  bool Save(bool inplace) {
    std::wstring target = path_;
    std::wstring temp = path_ + L".tmp";
    std::wstring outPath = inplace ? path_ : temp;
    HANDLE h = CreateFileW(outPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;

    std::vector<uint8_t> buf;
    uint32_t count = (uint32_t)entries_.size();
    buf.insert(buf.end(), {'G','R','F','2'}); // magic
    buf.resize(16);
    *(uint32_t*)&buf[12] = count;
    for (auto& kv : entries_) {
      const FileEntry& fe = kv.second;
      uint32_t len = (uint32_t)fe.pathOriginal.size();
      buf.insert(buf.end(), (uint8_t*)&len, (uint8_t*)&len+4);
      buf.insert(buf.end(), (uint8_t*)fe.pathOriginal.c_str(),
                 (uint8_t*)fe.pathOriginal.c_str()+len*2);
      uint32_t dlen = (uint32_t)fe.data.size();
      buf.insert(buf.end(), (uint8_t*)&dlen, (uint8_t*)&dlen+4);
      buf.insert(buf.end(), fe.data.begin(), fe.data.end());
    }
    DWORD written=0;
    WriteFile(h, buf.data(), (DWORD)buf.size(), &written, nullptr);
    FlushFileBuffers(h);
    CloseHandle(h);
    if (!inplace) {
      ReplaceFileW(path_.c_str(), temp.c_str(), nullptr, 0, nullptr, nullptr);
      DeleteFileW(temp.c_str());
    }
    return true;
  }

  void InsertOrReplace(const std::wstring& path, std::vector<uint8_t> data) {
    std::wstring key = ToLower(path);
    entries_[key] = FileEntry{path, std::move(data)};
  }

private:
  std::wstring path_;
  std::unordered_map<std::wstring, FileEntry> entries_;
};

bool WriteFileSafe(const std::wstring& path, const std::vector<uint8_t>& bytes) {
  std::filesystem::create_directories(std::filesystem::path(path).parent_path());
  HANDLE h = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                         FILE_ATTRIBUTE_NORMAL, nullptr);
  if (h == INVALID_HANDLE_VALUE) return false;
  DWORD written = 0;
  WriteFile(h, bytes.data(), (DWORD)bytes.size(), &written, nullptr);
  FlushFileBuffers(h);
  CloseHandle(h);
  return written == bytes.size();
}

} // namespace

// -----------------------------------------------------------------------------
// THOR parser (ASSF streamed or indexed formats)
// -----------------------------------------------------------------------------
namespace {

bool IsValidZlibHeader(const uint8_t* p) {
  if (!p) return false;
  if (p[0] != 0x78) return false;
  uint8_t flg = p[1];
  if (flg != 0x01 && flg != 0x5E && flg != 0x9C && flg != 0xDA) return false;
  return ((p[0] << 8) | flg) % 31 == 0;
}

// Inflate entire zlib stream to memory (dynamic size)
bool InflateAll(const uint8_t* src, size_t srcSize, std::vector<uint8_t>& out) {
  z_stream strm{};
  strm.next_in = const_cast<Bytef*>(src);
  strm.avail_in = (uInt)srcSize;
  if (inflateInit(&strm) != Z_OK) return false;
  const size_t CHUNK = 1 << 15;
  std::vector<uint8_t> tmp(CHUNK);
  while (true) {
    strm.next_out = tmp.data();
    strm.avail_out = (uInt)tmp.size();
    int ret = inflate(&strm, Z_NO_FLUSH);
    if (ret != Z_OK && ret != Z_STREAM_END) {
      inflateEnd(&strm);
      return false;
    }
    size_t produced = tmp.size() - strm.avail_out;
    out.insert(out.end(), tmp.data(), tmp.data() + produced);
    if (ret == Z_STREAM_END) break;
  }
  inflateEnd(&strm);
  return true;
}

std::wstring DecodePathBytes(const std::vector<uint8_t>& bytes) {
  auto tryCode = [&](UINT cp) -> std::wstring {
    int wlen = MultiByteToWideChar(cp, MB_ERR_INVALID_CHARS,
                                   reinterpret_cast<LPCCH>(bytes.data()),
                                   (int)bytes.size(), nullptr, 0);
    if (wlen <= 0) return {};
    std::wstring ws(wlen, L'\0');
    MultiByteToWideChar(cp, MB_ERR_INVALID_CHARS,
                        reinterpret_cast<LPCCH>(bytes.data()),
                        (int)bytes.size(), ws.data(), wlen);
    return ws;
  };
  std::wstring w = tryCode(949); // CP949
  if (w.empty()) w = tryCode(1252); // Windows-1252
  if (w.empty()) w = tryCode(CP_UTF8);
  if (w.empty()) w.assign(bytes.begin(), bytes.end());

  for (auto& c : w) if (c == L'\\') c = L'/';
  // collapse .. segments
  std::vector<std::wstring> parts;
  std::wstring cur;
  for (wchar_t ch : w + L"/") {
    if (ch == L'/') {
      if (cur == L"..") { if (!parts.empty()) parts.pop_back(); }
      else if (!cur.empty() && cur != L".") parts.push_back(cur);
      cur.clear();
    } else {
      cur.push_back(ch);
    }
  }
  std::wstring norm;
  for (size_t i = 0; i < parts.size(); ++i) {
    if (i) norm += L'/';
    norm += parts[i];
  }
  if (!norm.empty() && (norm[0] == L'/' || norm[0] == L'\\'))
    norm = L"data" + norm;
  return norm;
}

bool ParseThor(const std::wstring& path, std::vector<PatchEntry>& out) {
  HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                         OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
  if (h == INVALID_HANDLE_VALUE) return false;
  LARGE_INTEGER size{};
  GetFileSizeEx(h, &size);
  std::vector<uint8_t> file(size.QuadPart);
  DWORD read=0; ReadFile(h, file.data(), (DWORD)file.size(), &read, nullptr);
  CloseHandle(h);
  if (file.size() < 8) return false;

  // Header
  if (memcmp(file.data(), "ASSF", 4) != 0) return false;
  size_t pos = 4;
  uint32_t metaLen = *(uint32_t*)&file[pos]; pos += 4;
  if (pos + metaLen > file.size()) return false;
  // metadata ignored except advancing pos
  pos += metaLen;
  size_t metaEnd = pos;

  // -------- Attempt streamed mode --------
  const uint8_t* scan = file.data() + metaEnd;
  const uint8_t* end = file.data() + file.size();
  const uint8_t* zStart = nullptr;
  for (const uint8_t* p = scan; p + 2 <= end; ++p) {
    if (IsValidZlibHeader(p)) { zStart = p; break; }
  }
  if (zStart) {
    std::vector<uint8_t> dec;
    if (InflateAll(zStart, end - zStart, dec)) {
      size_t p = 0; size_t valid = 0;
      while (p + 8 <= dec.size()) {
        int32_t pathLen = *(int32_t*)&dec[p]; p += 4;
        int32_t dataLen = *(int32_t*)&dec[p]; p += 4;
        uint32_t flags = 0;
        if (dec.size() - p >= (size_t)pathLen + (size_t)dataLen + 4) {
          flags = *(uint32_t*)&dec[p];
          p += 4;
        }
        if (pathLen < 0 || dataLen <= 0) break;
        std::vector<uint8_t> pathBytes;
        if (dec.size() - p < (size_t)pathLen + (size_t)dataLen) break;
        if ((size_t)pathLen > dec.size() - p) {
          // fallback: C-string
          size_t start = p;
          while (p < dec.size() && dec[p] != 0) ++p;
          pathBytes.assign(dec.begin() + start, dec.begin() + p);
          ++p;
        } else {
          pathBytes.assign(dec.begin() + p, dec.begin() + p + pathLen);
          p += pathLen;
        }
        if (dec.size() - p < (size_t)dataLen) break;
        std::vector<uint8_t> data(dec.begin() + p, dec.begin() + p + dataLen);
        p += dataLen;
        std::wstring wpath = DecodePathBytes(pathBytes);
        if (!wpath.empty()) {
          PatchEntry pe;
          pe.logicalPath = wpath;
          pe.targetIsGrf = true;
          pe.bytes = std::move(data);
          out.push_back(std::move(pe));
          ++valid;
        }
      }
      if (!out.empty()) return true; // streamed succeeded
    }
  }

  // -------- Indexed mode --------
  const uint8_t* last = nullptr;
  for (const uint8_t* p = end - 2; p >= file.data() + metaEnd; --p) {
    if (IsValidZlibHeader(p)) { last = p; break; }
    if (p == file.data() + metaEnd) break;
  }
  if (!last) return false;

  std::vector<uint8_t> index;
  if (!InflateAll(last, end - last, index)) return false;
  size_t ip = 0;
  while (ip < index.size()) {
    uint8_t tag = index[ip++];
    if (tag == 0 || tag == 0xFF) break;
    size_t start = ip;
    while (ip < index.size() && index[ip] != 0) ++ip;
    if (ip >= index.size()) break;
    std::vector<uint8_t> pathBytes(index.begin()+start, index.begin()+ip);
    ++ip; // skip null
    if (ip + 16 > index.size()) break;
    uint32_t offset = *(uint32_t*)&index[ip]; ip +=4;
    uint32_t comp = *(uint32_t*)&index[ip]; ip +=4;
    uint32_t decomp = *(uint32_t*)&index[ip]; ip +=4;
    uint32_t crc = *(uint32_t*)&index[ip]; ip +=4; (void)crc;
    if (comp == 0 || offset + comp > file.size()) break;
    std::vector<uint8_t> dec;
    if (!InflateAll(file.data()+offset, comp, dec)) break;
    if (dec.size() != decomp) break;
    std::wstring wpath = DecodePathBytes(pathBytes);
    if (wpath.empty()) continue;
    PatchEntry pe;
    pe.logicalPath = wpath;
    pe.targetIsGrf = true;
    pe.bytes = std::move(dec);
    out.push_back(std::move(pe));
  }

  return !out.empty();
}

} // namespace

// -----------------------------------------------------------------------------
// MergeFolderIntoGrf
// -----------------------------------------------------------------------------
bool MergeFolderIntoGrf(const std::wstring& folderPath,
                        const std::wstring& defaultGrfPath,
                        const GrfPatchOptions& options,
                        IPatchObserver* observer) {
  std::vector<PatchEntry> entries;
  for (auto& p : std::filesystem::recursive_directory_iterator(folderPath)) {
    if (!p.is_regular_file()) continue;
    PatchEntry e;
    e.logicalPath = p.path().wstring();
    e.targetIsGrf = true;
    e.bytes.resize(std::filesystem::file_size(p));
    HANDLE h = CreateFileW(p.path().c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) continue;
    DWORD read=0;
    ReadFile(h, e.bytes.data(), (DWORD)e.bytes.size(), &read, nullptr);
    CloseHandle(h);
    entries.push_back(std::move(e));
  }

  std::unordered_map<std::wstring, std::vector<PatchEntry>> byGrf;
  for (auto& e : entries) {
    std::wstring grf = e.explicitGrf.empty() ? defaultGrfPath : e.explicitGrf;
    byGrf[grf].push_back(std::move(e));
  }

  size_t done = 0, total = entries.size();
  for (auto& kv : byGrf) {
    GrfFile g(kv.first);
    if (!g.Load() && !options.createIfMissing) {
      if (observer) observer->OnError("Missing GRF: " + std::string(kv.first.begin(), kv.first.end()));
      return false;
    }
    for (auto& pe : kv.second) {
      g.InsertOrReplace(pe.logicalPath, pe.bytes);
      ++done;
      if (observer) observer->OnInstallProgress(done, total);
    }
    g.Save(options.inPlace);
  }
  if (observer) observer->OnReady();
  return true;
}

// -----------------------------------------------------------------------------
// ApplyThorPatchToGrf
// -----------------------------------------------------------------------------
bool ApplyThorPatchToGrf(const std::wstring& thorPath,
                         const std::wstring& defaultGrfPath,
                         const GrfPatchOptions& options,
                         IPatchObserver* observer) {
  if (observer) observer->OnStatus("Header");
  std::vector<PatchEntry> entries;
  if (!ParseThor(thorPath, entries)) {
    if (observer) observer->OnError("Failed to parse THOR");
    return false;
  }
  if (observer) observer->OnStatus("Payload");

  std::unordered_map<std::wstring, std::vector<PatchEntry>> byGrf;
  std::vector<PatchEntry> fsEntries;
  for (auto& e : entries) {
    if (e.targetIsGrf) {
      std::wstring grf = e.explicitGrf.empty() ? defaultGrfPath : e.explicitGrf;
      byGrf[grf].push_back(std::move(e));
    } else {
      fsEntries.push_back(std::move(e));
    }
  }

  size_t total = entries.size();
  if (observer) {
    observer->OnStatus("Merging " + std::to_string(total) + " files");
  }
  size_t done = 0;

  for (auto& e : fsEntries) {
    std::wstring outPath = std::filesystem::path(defaultGrfPath).parent_path() / e.logicalPath;
    if (!WriteFileSafe(outPath, e.bytes)) {
      if (observer) observer->OnError("Failed writing " + std::string(outPath.begin(), outPath.end()));
      return false;
    }
    ++done;
    if (observer) observer->OnInstallProgress(done, total);
  }

  bool allSaved = true;
  for (auto& kv : byGrf) {
    GrfFile g(kv.first);
    if (!g.Load() && !options.createIfMissing) {
      if (observer) observer->OnError("Missing GRF: " + std::string(kv.first.begin(), kv.first.end()));
      return false;
    }
    for (auto& pe : kv.second) {
      g.InsertOrReplace(pe.logicalPath, pe.bytes);
      ++done;
      if (observer) observer->OnInstallProgress(done, total);
    }
    if (observer) observer->OnStatus("Save");
    if (!g.Save(options.inPlace)) {
      allSaved = false;
    }
  }
  if (!allSaved) return false;
  DeleteFileW(thorPath.c_str());
  if (observer) observer->OnReady();
  return true;
}
