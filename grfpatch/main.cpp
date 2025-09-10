#include "grf_patch.hpp"
#include <iostream>

class ConsoleObserver : public IPatchObserver {
public:
  void OnStatus(const std::string& msg) override { std::cout << "[STATUS] " << msg << std::endl; }
  void OnInstallProgress(size_t d, size_t t) override {
    std::cout << "Progress " << d << "/" << t << std::endl;
  }
  void OnError(const std::string& msg) override { std::cout << "[ERROR] " << msg << std::endl; }
  void OnReady() override { std::cout << "Patch complete" << std::endl; }
};

int main() {
  GrfPatchOptions opt{ false, true };
  ConsoleObserver obs;
  if (!ApplyThorPatchToGrf(L"patch_001.thor", L".\\data.grf", opt, &obs))
    return 1;
  return 0;
}
