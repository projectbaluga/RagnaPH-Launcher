#include "grf_patch.hpp"
#include <Windows.h>
#include <algorithm>
#include <filesystem>
#include <unordered_map>
#include <memory>
#include <cstdio>

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
// THOR parser (minimal custom format)
// -----------------------------------------------------------------------------
namespace {

bool ParseThor(const std::wstring& path, std::vector<PatchEntry>& out) {
  HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                         OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
  if (h == INVALID_HANDLE_VALUE) return false;
  LARGE_INTEGER size{};
  GetFileSizeEx(h, &size);
  std::vector<uint8_t> buf(size.QuadPart);
  DWORD read=0; ReadFile(h, buf.data(), (DWORD)buf.size(), &read, nullptr);
  CloseHandle(h);
  if (buf.size() < 8) return false;
  if (memcmp(buf.data(), "THOR", 4)!=0) return false;
  uint32_t count = *(uint32_t*)&buf[4];
  size_t pos = 8;
  for (uint32_t i=0; i<count; ++i) {
    if (pos + 13 > buf.size()) return false;
    uint8_t tgt = buf[pos++]; // 0 grf,1 fs
    uint32_t pathLen = *(uint32_t*)&buf[pos]; pos +=4;
    std::wstring wpath((wchar_t*)&buf[pos], pathLen); pos += pathLen*2;
    uint32_t grfLen = *(uint32_t*)&buf[pos]; pos +=4;
    std::wstring grf;
    if (grfLen) { grf.assign((wchar_t*)&buf[pos], grfLen); pos += grfLen*2; }
    uint8_t comp = buf[pos++];
    uint32_t dataLen = *(uint32_t*)&buf[pos]; pos +=4;
    std::vector<uint8_t> data(buf.begin()+pos, buf.begin()+pos+dataLen);
    pos += dataLen;
    if (comp) {
      Decompressor d;
      std::vector<uint8_t> dec;
      if (!d.Decompress(data.data(), data.size(), dec, *(uint32_t*)&dec)) {}
    }
    PatchEntry pe;
    pe.logicalPath = wpath;
    pe.targetIsGrf = tgt==0;
    pe.explicitGrf = grf;
    pe.bytes = std::move(data);
    out.push_back(std::move(pe));
  }
  return true;
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
  if (observer) observer->OnStatus("Opening THOR");
  std::vector<PatchEntry> entries;
  if (!ParseThor(thorPath, entries)) {
    if (observer) observer->OnError("Failed to parse THOR");
    return false;
  }
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
  size_t done = 0, total = entries.size();

  for (auto& e : fsEntries) {
    std::wstring outPath = std::filesystem::path(defaultGrfPath).parent_path() / e.logicalPath;
    if (!WriteFileSafe(outPath, e.bytes)) {
      if (observer) observer->OnError("Failed writing " + std::string(outPath.begin(), outPath.end()));
      return false;
    }
    ++done;
    if (observer) observer->OnInstallProgress(done, total);
  }

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
