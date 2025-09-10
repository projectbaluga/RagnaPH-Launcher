#pragma once
#include <string>
#include <vector>
#include <cstdint>

// -----------------------------------------------------------------------------
// Options
// -----------------------------------------------------------------------------
struct GrfPatchOptions {
  bool inPlace = false;        // false = safe rebuild; true = direct mutation
  bool createIfMissing = true; // create v0x200 when GRF not found
};

// -----------------------------------------------------------------------------
// Patch entry
// -----------------------------------------------------------------------------
struct PatchEntry {
  std::wstring logicalPath;    // e.g., L"data\\texture\\foo.bmp"
  bool targetIsGrf;            // true → merge to GRF, false → write to FS
  std::wstring explicitGrf;    // if empty and targetIsGrf, use default GRF
  std::vector<uint8_t> bytes;  // decompressed content
};

// -----------------------------------------------------------------------------
// Observer interface
// -----------------------------------------------------------------------------
class IPatchObserver {
public:
  virtual ~IPatchObserver() = default;
  virtual void OnStatus(const std::string& msg) = 0;
  virtual void OnInstallProgress(size_t done, size_t total) = 0;
  virtual void OnError(const std::string& msg) = 0;
  virtual void OnReady() = 0;
};

// -----------------------------------------------------------------------------
// API
// -----------------------------------------------------------------------------
bool ApplyThorPatchToGrf(const std::wstring& thorPath,
                         const std::wstring& defaultGrfPath,
                         const GrfPatchOptions& options,
                         IPatchObserver* observer);

bool MergeFolderIntoGrf(const std::wstring& folderPath,
                        const std::wstring& defaultGrfPath,
                        const GrfPatchOptions& options,
                        IPatchObserver* observer);

