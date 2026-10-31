// Standalone test-only socket client. CI copies this as firefox.exe to test WFP app-scoped rules.
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <iostream>
#pragma comment(lib, "Ws2_32.lib")
int wmain(int argc, wchar_t** argv) {
  if(argc!=2){std::cerr<<"usage: FHGProbe <port>\n";return 2;}
  int port=_wtoi(argv[1]);
  if(port<1||port>65535) return 2;
  WSADATA d{};
  if(WSAStartup(MAKEWORD(2,2),&d)!=0) return 2;
  SOCKET s=socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
  if(s==INVALID_SOCKET){WSACleanup();return 2;}
  DWORD milliseconds=2500;
  setsockopt(s,SOL_SOCKET,SO_RCVTIMEO,reinterpret_cast<const char*>(&milliseconds),sizeof(milliseconds));
  sockaddr_in remote{};
  remote.sin_family=AF_INET;
  remote.sin_addr.s_addr=htonl(0x7f000001);
  remote.sin_port=htons(static_cast<u_short>(port));
  int rc=connect(s,reinterpret_cast<const sockaddr*>(&remote),sizeof(remote));
  if(rc==0) std::cout<<"CONNECTED "<<port<<"\n";
  else std::cout<<"BLOCKED/FAILED "<<port<<" WSA="<<WSAGetLastError()<<"\n";
  closesocket(s);
  WSACleanup();
  return rc==0?0:1;
}
