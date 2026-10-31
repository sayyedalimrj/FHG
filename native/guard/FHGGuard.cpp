// FHGGuard: Windows WFP policy prototype. Build & runtime testing required.
// Persistent, executable-path-scoped *block* rules. Never edits global firewall profiles.
// This tool does not launch Firefox and is not a finished zero-leak product.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <fwpmu.h>
#include <fwptypes.h>
#include <filesystem>
#include <iostream>
#include <vector>
#include <set>
#include <string>
#include <algorithm>
#include <cwctype>
#include <stdexcept>
#include <cstring>
#pragma comment(lib, "Fwpuclnt.lib")

namespace fs = std::filesystem;
namespace {
const GUID kProvider = {0xad104a20,0x5de6,0x45ad,{0x91,0x52,0xbc,0x89,0x70,0x60,0x52,0xa1}};
const GUID kSublayer = {0xec768632,0x7c86,0x49b4,{0x84,0x18,0x39,0x19,0xbb,0x61,0xa1,0x23}};
constexpr UINT16 kPort = 12334;
constexpr UINT32 kLoopback = 0x7f000001U; // WFP FWP_UINT32 IPv4 values use host order.
constexpr UINT8 kTcp = 6;
const wchar_t* kTitle = L"FHG isolated Firefox egress guard";

struct Engine {
  HANDLE handle = nullptr;
  Engine() {
    FWPM_SESSION0 s{};
    s.displayData.name = const_cast<wchar_t*>(kTitle);
    // Do NOT use FWPM_SESSION_FLAG_DYNAMIC: rules must remain after process exit.
    DWORD code = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_DEFAULT, nullptr, &s, &handle);
    if (code) throw std::runtime_error("FwpmEngineOpen0: " + std::to_string(code));
  }
  ~Engine() { if (handle) FwpmEngineClose0(handle); }
  Engine(const Engine&) = delete;
  Engine& operator=(const Engine&) = delete;
};

void expect(DWORD code, const char* context) {
  if (code) throw std::runtime_error(std::string(context) + " WFP code " + std::to_string(code));
}
bool same(const GUID& a, const GUID& b) { return InlineIsEqualGUID(a, b) != 0; }
std::wstring lower(std::wstring s) {
  std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c) { return std::towlower(c); });
  return s;
}
std::vector<std::wstring> browserExecutables(const std::wstring& folder) {
  fs::path root = fs::weakly_canonical(fs::absolute(fs::path(folder)));
  if (!fs::exists(root / L"firefox.exe"))
    throw std::runtime_error("Expected firefox.exe directly in the supplied folder");
  std::vector<std::wstring> paths;
  for (fs::recursive_directory_iterator it(root, fs::directory_options::skip_permission_denied), end;
       it != end; ++it) {
    std::error_code ec;
    if (!it->is_regular_file(ec) || lower(it->path().extension().wstring()) != L".exe") continue;
    const auto path = fs::weakly_canonical(it->path()).wstring();
    // Symlinks and junctions could point out of the intended installation.
    const std::wstring parent = lower(root.wstring() + L"\\");
    if (lower(path).rfind(parent, 0) != 0)
      throw std::runtime_error("Executable resolves outside the supplied folder");
    paths.push_back(path);
  }
  std::sort(paths.begin(), paths.end());
  paths.erase(std::unique(paths.begin(), paths.end()), paths.end());
  if (paths.empty()) throw std::runtime_error("No executables found");
  return paths;
}
// Deterministic filter IDs per path + block reason. Key consistency enables read-only status.
GUID filterKey(const std::wstring& filename, unsigned reason) {
  uint64_t h1=0xcbf29ce484222325ULL, h2=0x84222325cbf29ce4ULL;
  std::wstring s = lower(filename) + L"#" + std::to_wstring(reason);
  for (wchar_t c : s) {
    const uint16_t v = static_cast<uint16_t>(c);
    for (unsigned b=0;b<2;++b) {
      const uint8_t byte = static_cast<uint8_t>(v >> (8*b));
      h1=(h1 ^ byte)*0x100000001b3ULL;
      h2=(h2 ^ byte)*0x100000001b3ULL;
    }
  }
  GUID g{};
  std::memcpy(&g, &h1, 8);
  std::memcpy(reinterpret_cast<char*>(&g)+8, &h2, 8);
  return g;
}
struct Blob {
  FWP_BYTE_BLOB* p = nullptr;
  explicit Blob(const std::wstring& path) {
    expect(FwpmGetAppIdFromFileName0(path.c_str(), &p), "FwpmGetAppIdFromFileName0");
  }
  ~Blob() { if(p) FwpmFreeMemory0(reinterpret_cast<void**>(&p)); }
  Blob(const Blob&) = delete;
  Blob& operator=(const Blob&) = delete;
};
FWPM_FILTER_CONDITION0 appCondition(FWP_BYTE_BLOB* b) {
  FWPM_FILTER_CONDITION0 c{};
  c.fieldKey=FWPM_CONDITION_ALE_APP_ID;
  c.matchType=FWP_MATCH_EQUAL;
  c.conditionValue.type=FWP_BYTE_BLOB_TYPE;
  c.conditionValue.byteBlob=b;
  return c;
}
FWPM_FILTER_CONDITION0 otherCondition(unsigned reason) {
  FWPM_FILTER_CONDITION0 c{};
  c.matchType=FWP_MATCH_NOT_EQUAL;
  if (reason==0) {
    c.fieldKey=FWPM_CONDITION_IP_REMOTE_ADDRESS;
    c.conditionValue.type=FWP_UINT32;
    c.conditionValue.uint32=kLoopback;
  } else if (reason==1) {
    c.fieldKey=FWPM_CONDITION_IP_REMOTE_PORT;
    c.conditionValue.type=FWP_UINT16;
    c.conditionValue.uint16=kPort;
  } else {
    c.fieldKey=FWPM_CONDITION_IP_PROTOCOL;
    c.conditionValue.type=FWP_UINT8;
    c.conditionValue.uint8=kTcp;
  }
  return c;
}
void addRule(HANDLE h, const std::wstring& path, unsigned reason, FWP_BYTE_BLOB* app) {
  FWPM_FILTER_CONDITION0 cs[2]{};
  cs[0]=appCondition(app);
  const bool ipv6=reason==3;
  if (!ipv6) cs[1]=otherCondition(reason);
  FWPM_FILTER0 f{};
  f.filterKey=filterKey(path,reason);
  f.displayData.name=const_cast<wchar_t*>(kTitle);
  f.flags=FWPM_FILTER_FLAG_PERSISTENT;
  f.providerKey=const_cast<GUID*>(&kProvider);
  f.subLayerKey=kSublayer;
  f.layerKey=ipv6?FWPM_LAYER_ALE_AUTH_CONNECT_V6:FWPM_LAYER_ALE_AUTH_CONNECT_V4;
  f.action.type=FWP_ACTION_BLOCK;
  f.weight.type=FWP_UINT8;
  f.weight.uint8=15;
  f.numFilterConditions=ipv6?1:2;
  f.filterCondition=cs;
  expect(FwpmFilterAdd0(h,&f,nullptr,nullptr), "FwpmFilterAdd0");
}
bool validRule(HANDLE h, const std::wstring& path, unsigned reason, FWP_BYTE_BLOB* app) {
  auto key=filterKey(path,reason);
  FWPM_FILTER0* f=nullptr;
  DWORD code=FwpmFilterGetByKey0(h,&key,&f);
  if(code) return false;
  bool ok=same(f->subLayerKey,kSublayer) && f->providerKey &&
      same(*f->providerKey,kProvider) &&
      f->action.type==FWP_ACTION_BLOCK &&
      (f->flags & FWPM_FILTER_FLAG_PERSISTENT) &&
      same(f->layerKey, reason==3? FWPM_LAYER_ALE_AUTH_CONNECT_V6:FWPM_LAYER_ALE_AUTH_CONNECT_V4) &&
      f->numFilterConditions==(reason==3?1u:2u);
  if (ok) {
    const auto& ac=f->filterCondition[0];
    ok=same(ac.fieldKey,FWPM_CONDITION_ALE_APP_ID) &&
       ac.matchType==FWP_MATCH_EQUAL &&
       ac.conditionValue.type==FWP_BYTE_BLOB_TYPE &&
       ac.conditionValue.byteBlob &&
       ac.conditionValue.byteBlob->size==app->size &&
       std::memcmp(ac.conditionValue.byteBlob->data,app->data,app->size)==0;
  }
  if (ok && reason!=3) {
    const auto expected=otherCondition(reason);
    const auto& c=f->filterCondition[1];
    ok=same(c.fieldKey,expected.fieldKey) && c.matchType==FWP_MATCH_NOT_EQUAL &&
       c.conditionValue.type==expected.conditionValue.type &&
       ((reason==0 && c.conditionValue.uint32==kLoopback) ||
        (reason==1 && c.conditionValue.uint16==kPort) ||
        (reason==2 && c.conditionValue.uint8==kTcp));
  }
  FwpmFreeMemory0(reinterpret_cast<void**>(&f));
  return ok;
}
std::vector<GUID> allFilterKeys(HANDLE h) {
  // WFP enumeration requires an explicit layer: zero GUID gives FWP_E_LAYER_NOT_FOUND.
  std::vector<GUID> result;
  const GUID layers[2] = {FWPM_LAYER_ALE_AUTH_CONNECT_V4, FWPM_LAYER_ALE_AUTH_CONNECT_V6};
  for (const auto& layer : layers) {
    FWPM_FILTER_ENUM_TEMPLATE0 criteria{};
    criteria.providerKey=const_cast<GUID*>(&kProvider);
    criteria.layerKey=layer;
    criteria.enumType=FWP_FILTER_ENUM_FULLY_CONTAINED;
    criteria.actionMask=0xffffffffU;
    HANDLE e=nullptr;
    expect(FwpmFilterCreateEnumHandle0(h,&criteria,&e),"FwpmFilterCreateEnumHandle0");
    while (true) {
      FWPM_FILTER0** batch=nullptr;
      UINT32 n=0;
      DWORD code=FwpmFilterEnum0(h,e,128,&batch,&n);
      if(code) {
        FwpmFilterDestroyEnumHandle0(h,e);
        expect(code,"FwpmFilterEnum0");
      }
      for (UINT32 i=0;i<n;++i) result.push_back(batch[i]->filterKey);
      if(batch) FwpmFreeMemory0(reinterpret_cast<void**>(&batch));
      if(n==0) break;
    }
    expect(FwpmFilterDestroyEnumHandle0(h,e),"FwpmFilterDestroyEnumHandle0");
  }
  return result;
}
void ensureProvider(HANDLE h) {
  FWPM_PROVIDER0 p{};
  p.providerKey=kProvider;
  p.displayData.name=const_cast<wchar_t*>(kTitle);
  p.flags=FWPM_PROVIDER_FLAG_PERSISTENT;
  DWORD code=FwpmProviderAdd0(h,&p,nullptr);
  if(code && code!=FWP_E_ALREADY_EXISTS) expect(code,"FwpmProviderAdd0");
  FWPM_SUBLAYER0 s{};
  s.subLayerKey=kSublayer;
  s.providerKey=const_cast<GUID*>(&kProvider);
  s.displayData.name=const_cast<wchar_t*>(kTitle);
  s.flags=FWPM_SUBLAYER_FLAG_PERSISTENT;
  s.weight=0xffff;
  code=FwpmSubLayerAdd0(h,&s,nullptr);
  if(code && code!=FWP_E_ALREADY_EXISTS) expect(code,"FwpmSubLayerAdd0");
}
bool verify(HANDLE h,const std::vector<std::wstring>& paths) {
  FWPM_PROVIDER0* p=nullptr;
  FWPM_SUBLAYER0* s=nullptr;
  if(FwpmProviderGetByKey0(h,&kProvider,&p)!=ERROR_SUCCESS) return false;
  const bool providerOk=(p->flags & FWPM_PROVIDER_FLAG_PERSISTENT)!=0;
  FwpmFreeMemory0(reinterpret_cast<void**>(&p));
  if(!providerOk || FwpmSubLayerGetByKey0(h,&kSublayer,&s)!=ERROR_SUCCESS) return false;
  bool ok=(s->flags & FWPM_SUBLAYER_FLAG_PERSISTENT) && s->providerKey &&
          same(*s->providerKey,kProvider);
  FwpmFreeMemory0(reinterpret_cast<void**>(&s));
  if(!ok) return false;
  std::set<std::string> expected;
  for (const auto& path:paths) {
    Blob app(path);
    for(unsigned reason=0;reason<4;++reason) {
      if(!validRule(h,path,reason,app.p)) return false;
      const GUID id=filterKey(path,reason);
      expected.insert(std::string(reinterpret_cast<const char*>(&id),sizeof(id)));
    }
  }
  const auto installed=allFilterKeys(h);
  if(installed.size()!=expected.size()) return false;
  for(const auto& id:installed) {
    if(!expected.count(std::string(reinterpret_cast<const char*>(&id),sizeof(id)))) return false;
  }
  return true;
}
void install(HANDLE h,const std::vector<std::wstring>& paths) {
  expect(FwpmTransactionBegin0(h,0),"FwpmTransactionBegin0");
  bool active=true;
  try {
    ensureProvider(h);
    for(const auto& id:allFilterKeys(h))
      expect(FwpmFilterDeleteByKey0(h,&id),"FwpmFilterDeleteByKey0");
    for(const auto& path:paths) {
      Blob app(path);
      for(unsigned reason=0;reason<4;++reason) addRule(h,path,reason,app.p);
    }
    expect(FwpmTransactionCommit0(h),"FwpmTransactionCommit0");
    active=false;
  } catch(...) {
    if(active) FwpmTransactionAbort0(h);
    throw;
  }
  if(!verify(h,paths)) throw std::runtime_error("Post-install guard verification failed");
}
void remove(HANDLE h) {
  expect(FwpmTransactionBegin0(h,0),"FwpmTransactionBegin0");
  bool active=true;
  try {
    for(const auto& id:allFilterKeys(h))
      expect(FwpmFilterDeleteByKey0(h,&id),"FwpmFilterDeleteByKey0");
    DWORD c=FwpmSubLayerDeleteByKey0(h,&kSublayer);
    if(c && c!=FWP_E_SUBLAYER_NOT_FOUND) expect(c,"FwpmSubLayerDeleteByKey0");
    c=FwpmProviderDeleteByKey0(h,&kProvider);
    if(c && c!=FWP_E_PROVIDER_NOT_FOUND) expect(c,"FwpmProviderDeleteByKey0");
    expect(FwpmTransactionCommit0(h),"FwpmTransactionCommit0");
    active=false;
  } catch(...) {
    if(active) FwpmTransactionAbort0(h);
    throw;
  }
}
}
int wmain(int argc, wchar_t** argv) {
  try {
    if(argc<2 || (std::wstring(argv[1])!=L"remove" && argc!=3)) {
      std::wcerr << L"Usage: FHGGuard.exe install|status <FHG Firefox binary directory>\n"
                    L"       FHGGuard.exe remove  (requires elevation)\n";
      return 2;
    }
    std::wstring cmd=argv[1];
    std::vector<std::wstring> paths;
    if(cmd!=L"remove") paths=browserExecutables(argv[2]);
    Engine e;
    if(cmd==L"install") {
      install(e.handle, paths);
      std::wcout << L"FHG WFP rules installed for " << paths.size() << L" executables.\n";
    } else if(cmd==L"status") {
      if(!verify(e.handle,paths)) {
        std::wcerr << L"FHG guard missing, changed or incomplete. REFUSE browser network startup.\n";
        return 1;
      }
      std::wcout << L"FHG guard filter records verified for " << paths.size()
                 << L" executables. Runtime leak test still required.\n";
    } else if(cmd==L"remove") {
      remove(e.handle);
      std::wcout << L"FHG WFP rules removed.\n";
    } else {
      std::wcerr << L"Unknown command\n"; return 2;
    }
    return 0;
  } catch(const std::exception& e) {
    std::cerr << "FHGGuard: " << e.what() << "\n";
    return 1;
  }
}
