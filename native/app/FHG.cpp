// FHG experimental integrated controller, Windows x64.
// Local UI + supervised HiddifyCli + isolated Firefox. Never change global proxy.
#define UNICODE
#define _UNICODE
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <shellapi.h>
#include <commdlg.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <objbase.h>
#include <iphlpapi.h>
#include <tcpmib.h>
#include <filesystem>
#include <string>
#include <vector>
#include <cstdio>
#pragma comment(lib,"Shell32.lib")
#pragma comment(lib,"Comdlg32.lib")
#pragma comment(lib,"Iphlpapi.lib")
#pragma comment(lib,"Ole32.lib")
namespace fs=std::filesystem;
enum { E_CONFIG=110, E_STATUS=111, B_BROWSE=120, B_CONNECT=121,
       B_DISCONNECT=122, B_BROWSER=123, B_GUARD=124, B_OPTIONS=125 };
static HWND wnd=nullptr, cfgBox=nullptr, statusBox=nullptr;
static fs::path root, coreExe, firefoxExe, guardExe, browserDir, profileDir, stateDir;
static PROCESS_INFORMATION corePi{};
static bool guardOK=false;
static std::wstring configFile;
static const wchar_t* cls=L"FHG-Controller";
static std::wstring quote(const std::wstring& v) {
  std::wstring r=L"\"";
  for(wchar_t c:v){if(c==L'\"')r+=L'\\';r+=c;}return r+L"\"";
}
static void status(const std::wstring& s){if(statusBox)SetWindowTextW(statusBox,s.c_str());}
static void alert(const std::wstring& s){MessageBoxW(wnd,s.c_str(),L"FHG",MB_OK|MB_ICONWARNING);}
static void persistConfig(){
  std::error_code ec;fs::create_directories(stateDir,ec);
  FILE* f=nullptr;_wfopen_s(&f,(stateDir/L"config-path.txt").c_str(),L"wt, ccs=UTF-8");
  if(f){fputws(configFile.c_str(),f);fclose(f);}
}
static void createShortcut(const std::wstring& exePath){
  // Per-user Desktop only. Never overwrite another shortcut.
  const HRESULT init=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
  if(FAILED(init) && init!=RPC_E_CHANGED_MODE)return;
  PWSTR desktop=nullptr;
  if(SUCCEEDED(SHGetKnownFolderPath(FOLDERID_Desktop,0,nullptr,&desktop))){
    fs::path shortcut=fs::path(desktop)/L"FHG Browser.lnk";
    CoTaskMemFree(desktop);
    if(!fs::exists(shortcut)){
      IShellLinkW* link=nullptr;
      if(SUCCEEDED(CoCreateInstance(CLSID_ShellLink,nullptr,CLSCTX_INPROC_SERVER,
                                    IID_PPV_ARGS(&link)))){
        link->SetPath(exePath.c_str());
        link->SetWorkingDirectory(root.c_str());
        link->SetDescription(L"FHG: isolated Firefox and Hiddify");
        link->SetIconLocation(exePath.c_str(),0);
        IPersistFile* persist=nullptr;
        if(SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&persist)))){
          persist->Save(shortcut.c_str(),TRUE);
          persist->Release();
        }
        link->Release();
      }
    }
  }
  if(SUCCEEDED(init))CoUninitialize();
}
static void restoreConfig(){
  FILE* f=nullptr;_wfopen_s(&f,(stateDir/L"config-path.txt").c_str(),L"rt, ccs=UTF-8");
  if(!f)return;
  wchar_t buf[32768]={}; if(fgetws(buf,32768,f))configFile=buf;
  fclose(f);
  while(!configFile.empty() && (configFile.back()==L'\r'||configFile.back()==L'\n'))configFile.pop_back();
}
static bool startProcess(const fs::path& exe,const std::wstring& args,
                         const fs::path& working,PROCESS_INFORMATION& pi,DWORD flags){
  std::wstring cmd=quote(exe.wstring())+L" "+args;
  std::vector<wchar_t> mutableCmd(cmd.begin(),cmd.end()); mutableCmd.push_back(0);
  STARTUPINFOW si{};si.cb=sizeof(si);si.dwFlags=STARTF_USESHOWWINDOW;
  si.wShowWindow=(flags&CREATE_NO_WINDOW)?SW_HIDE:SW_SHOW;
  ZeroMemory(&pi,sizeof(pi));
  return CreateProcessW(exe.c_str(),mutableCmd.data(),nullptr,nullptr,FALSE,flags,
                        nullptr,working.c_str(),&si,&pi)!=FALSE;
}
static bool runGuard(const wchar_t* cmd, bool elevate){
  std::wstring args=std::wstring(cmd)+L" "+quote(browserDir.wstring());
  if(elevate){
    SHELLEXECUTEINFOW sh{}; sh.cbSize=sizeof(sh);
    sh.fMask=SEE_MASK_NOCLOSEPROCESS|SEE_MASK_FLAG_NO_UI;
    sh.hwnd=wnd; sh.lpVerb=L"runas";sh.lpFile=guardExe.c_str();
    sh.lpParameters=args.c_str();sh.lpDirectory=root.c_str();sh.nShow=SW_HIDE;
    if(!ShellExecuteExW(&sh))return false;
    WaitForSingleObject(sh.hProcess,INFINITE);
    DWORD code=1;GetExitCodeProcess(sh.hProcess,&code);
    CloseHandle(sh.hProcess);return code==0;
  }
  PROCESS_INFORMATION pi{};
  if(!startProcess(guardExe,args,root,pi,CREATE_NO_WINDOW))return false;
  WaitForSingleObject(pi.hProcess,15000);
  DWORD code=1;GetExitCodeProcess(pi.hProcess,&code);
  CloseHandle(pi.hThread);CloseHandle(pi.hProcess);
  return code==0;
}
static bool checkGuard(){return guardOK=runGuard(L"status",false);}
static bool setupGuard(){
  if(checkGuard())return true;
  status(L"Guard not installed. Windows permission is needed once.");
  if(!runGuard(L"install",true)){status(L"Guard setup failed. Firefox remains unavailable.");return false;}
  if(!checkGuard()){status(L"Guard verification failed. No Firefox start.");return false;}
  status(L"Network guard verified. Browser can open even while Hiddify is offline.");
  return true;
}
static bool coreRunning(){
  if(!corePi.hProcess)return false;
  return WaitForSingleObject(corePi.hProcess,0)==WAIT_TIMEOUT;
}
static void stopCore(){
  if(corePi.hProcess){
    // Core is a supervised subprocess; terminate only OUR process, not other instances.
    if(coreRunning()) {
      TerminateProcess(corePi.hProcess,0);
      WaitForSingleObject(corePi.hProcess,3000);
    }
    CloseHandle(corePi.hThread);CloseHandle(corePi.hProcess);
    ZeroMemory(&corePi,sizeof(corePi));
  }
}
static bool ownsProxyPort(DWORD pid){
  DWORD bytes=0;
  DWORD e=GetExtendedTcpTable(nullptr,&bytes,FALSE,AF_INET,
            TCP_TABLE_OWNER_PID_LISTENER,0);
  if(e!=ERROR_INSUFFICIENT_BUFFER)return false;
  std::vector<unsigned char> mem(bytes);
  e=GetExtendedTcpTable(mem.data(),&bytes,FALSE,AF_INET,TCP_TABLE_OWNER_PID_LISTENER,0);
  if(e!=NO_ERROR)return false;
  auto* tab=reinterpret_cast<PMIB_TCPTABLE_OWNER_PID>(mem.data());
  for(DWORD i=0;i<tab->dwNumEntries;++i){
    auto& x=tab->table[i];
    unsigned long a=ntohl(x.dwLocalAddr);
    if(x.dwOwningPid==pid && (a==0x7f000001 || a==0) &&
       ntohs(static_cast<u_short>(x.dwLocalPort))==12334)return true;
  }
  return false;
}
static bool foreignProxyPort(){
  DWORD bytes=0;
  if(GetExtendedTcpTable(nullptr,&bytes,FALSE,AF_INET,
      TCP_TABLE_OWNER_PID_LISTENER,0)!=ERROR_INSUFFICIENT_BUFFER)return false;
  std::vector<unsigned char> mem(bytes);
  if(GetExtendedTcpTable(mem.data(),&bytes,FALSE,AF_INET,
      TCP_TABLE_OWNER_PID_LISTENER,0)!=NO_ERROR)return false;
  auto* tab=reinterpret_cast<PMIB_TCPTABLE_OWNER_PID>(mem.data());
  for(DWORD i=0;i<tab->dwNumEntries;++i){
    auto& x=tab->table[i];
    auto p=ntohs(static_cast<u_short>(x.dwLocalPort));
    auto a=ntohl(x.dwLocalAddr);
    if(p==12334 && (a==0x7f000001 || a==0) &&
       (!coreRunning() || x.dwOwningPid!=corePi.dwProcessId))return true;
  }
  return false;
}
static void startCore(){
  if(coreRunning()){status(L"Hiddify Core is already running.");return;}
  wchar_t buf[32768]={};
  GetWindowTextW(cfgBox,buf,32768);configFile=buf;
  if(configFile.empty() || !fs::is_regular_file(fs::path(configFile))){
    alert(L"Choose an existing Hiddify configuration file first.");return;
  }
  if(!fs::exists(coreExe)){alert(L"Core\\HiddifyCli.exe was not found.");return;}
  if(foreignProxyPort()){alert(L"Port 12334 is owned by another process. Close it before connecting.");return;}
  persistConfig();
  // Hiddify official CLI syntax. Do not enable system proxy or TUN.
  std::wstring args=L"run -c "+quote(configFile);
  const fs::path extra=stateDir/L"hiddify-options.json";
  if(fs::is_regular_file(extra))args+=L" -d "+quote(extra.wstring());
  if(!startProcess(coreExe,args,coreExe.parent_path(),corePi,CREATE_NO_WINDOW)){
    alert(L"Could not launch HiddifyCli.");return;
  }
  status(L"Hiddify Core starting. Browser remains offline until port 12334 becomes available.");
}
static void browseConfig(){
  wchar_t name[32768]={};
  OPENFILENAMEW of{};of.lStructSize=sizeof(of);of.hwndOwner=wnd;
  of.lpstrFile=name;of.nMaxFile=32768;
  of.lpstrFilter=L"Config files\0*.json;*.yaml;*.yml;*.txt\0All files\0*.*\0\0";
  of.Flags=OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST;
  if(GetOpenFileNameW(&of)){configFile=name;SetWindowTextW(cfgBox,name);persistConfig();}
}
static void openBrowser(){
  if(!setupGuard()){alert(L"Network guard has not passed verification. Browser was not started.");return;}
  if(!fs::exists(firefoxExe)){alert(L"Firefox\\firefox.exe is missing.");return;}
  // The profile is exclusive to FHG; -no-remote prevents attaching to normal Firefox.
  std::error_code ec;fs::create_directories(profileDir,ec);
  if(ec){alert(L"Cannot create isolated browser profile.");return;}
  std::wstring args=L"-no-remote -profile "+quote(profileDir.wstring())+L" about:blank";
  PROCESS_INFORMATION pi{};
  if(!startProcess(firefoxExe,args,browserDir,pi,0)){
    alert(L"Failed to launch Firefox.");return;
  }
  CloseHandle(pi.hThread);CloseHandle(pi.hProcess);
  status(L"Browser opened. Its network access is restricted to Hiddify's local port.");
}
static void chooseOptions(){
  alert(L"For an optional HiddifyApp configuration, copy your exported settings to Data\\hiddify-options.json. Do not put credentials into this public repository.");
}
static LRESULT CALLBACK windowProc(HWND h,UINT msg,WPARAM w,LPARAM l){
  switch(msg){
    case WM_CREATE: {
      HFONT font=static_cast<HFONT>(GetStockObject(DEFAULT_GUI_FONT));
      CreateWindowW(L"STATIC",L"FHG  |  Firefox + Hiddify",WS_CHILD|WS_VISIBLE,
                    22,20,430,30,h,nullptr,nullptr,nullptr);
      CreateWindowW(L"STATIC",L"Hiddify configuration file:",WS_CHILD|WS_VISIBLE,
                    22,65,320,24,h,nullptr,nullptr,nullptr);
      cfgBox=CreateWindowExW(WS_EX_CLIENTEDGE,L"EDIT",configFile.c_str(),
           WS_CHILD|WS_VISIBLE|ES_AUTOHSCROLL,22,90,390,28,h,(HMENU)E_CONFIG,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Browse",WS_CHILD|WS_VISIBLE,
                    422,90,94,28,h,(HMENU)B_BROWSE,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Connect Hiddify",WS_CHILD|WS_VISIBLE,
                    22,137,155,40,h,(HMENU)B_CONNECT,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Disconnect",WS_CHILD|WS_VISIBLE,
                    187,137,135,40,h,(HMENU)B_DISCONNECT,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Open browser",WS_CHILD|WS_VISIBLE,
                    332,137,185,40,h,(HMENU)B_BROWSER,nullptr,nullptr);
      statusBox=CreateWindowW(L"STATIC",L"Checking browser guard...",
             WS_CHILD|WS_VISIBLE|SS_LEFT,22,204,494,60,h,(HMENU)E_STATUS,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Guard setup",WS_CHILD|WS_VISIBLE,
                    22,282,115,29,h,(HMENU)B_GUARD,nullptr,nullptr);
      CreateWindowW(L"BUTTON",L"Core options",WS_CHILD|WS_VISIBLE,
                    146,282,115,29,h,(HMENU)B_OPTIONS,nullptr,nullptr);
      for(HWND child=GetWindow(h,GW_CHILD);child;child=GetWindow(child,GW_HWNDNEXT))
        SendMessageW(child,WM_SETFONT,(WPARAM)font,TRUE);
      SetTimer(h,1,1800,nullptr);
      status(checkGuard()?L"Guard verified. Browser may open.":L"Guard not installed yet; browser launch will request one-time permission.");
      return 0;
    }
    case WM_TIMER:
      if(coreRunning()){
        status(ownsProxyPort(corePi.dwProcessId)
             ?L"Hiddify Core listening at 127.0.0.1:12334."
             :L"Hiddify Core running, but local port 12334 not yet owned by it.");
      }
      return 0;
    case WM_COMMAND:
      switch(LOWORD(w)){
        case B_BROWSE:browseConfig();break;
        case B_CONNECT:startCore();break;
        case B_DISCONNECT:stopCore();status(L"Hiddify disconnected. Browser tabs stay open; no direct fallback.");break;
        case B_BROWSER:openBrowser();break;
        case B_GUARD:setupGuard();break;
        case B_OPTIONS:chooseOptions();break;
      }
      return 0;
    case WM_DESTROY:
      KillTimer(h,1);stopCore();PostQuitMessage(0);return 0;
  }
  return DefWindowProcW(h,msg,w,l);
}
int WINAPI wWinMain(HINSTANCE ins,HINSTANCE,LPWSTR,int show){
  wchar_t exe[MAX_PATH]={};GetModuleFileNameW(nullptr,exe,MAX_PATH);
  root=fs::path(exe).parent_path();
  browserDir=root/L"Firefox"; firefoxExe=browserDir/L"firefox.exe";
  coreExe=root/L"Core"/L"HiddifyCli.exe"; guardExe=root/L"FHGGuard.exe";
  stateDir=root/L"Data"; profileDir=stateDir/L"FirefoxProfile";
  restoreConfig();
  createShortcut(exe);
  WNDCLASSEXW wc{};wc.cbSize=sizeof(wc);wc.hInstance=ins;
  wc.lpszClassName=cls;wc.lpfnWndProc=windowProc;
  wc.hIcon=LoadIconW(ins,MAKEINTRESOURCEW(101));
  wc.hIconSm=LoadIconW(ins,MAKEINTRESOURCEW(101));
  wc.hCursor=LoadCursorW(nullptr,IDC_ARROW);
  wc.hbrBackground=(HBRUSH)(COLOR_WINDOW+1);
  if(!RegisterClassExW(&wc))return 2;
  wnd=CreateWindowExW(0,cls,L"FHG Secure Browser — Experimental",WS_OVERLAPPED|WS_CAPTION|
         WS_SYSMENU|WS_MINIMIZEBOX,CW_USEDEFAULT,CW_USEDEFAULT,555,365,
         nullptr,nullptr,ins,nullptr);
  if(!wnd)return 3;
  ShowWindow(wnd,show);UpdateWindow(wnd);
  MSG msg{};while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
  return (int)msg.wParam;
}
