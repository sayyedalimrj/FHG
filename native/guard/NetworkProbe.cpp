// Standalone socket probe for WFP integration tests; not bundled to end users.
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <iostream>
#include <string>
#pragma comment(lib, "Ws2_32.lib")
int wmain(int argc, wchar_t** argv) {
  // FHGProbe <port> [ipv6] ; destinations are always loopback.
  if(argc<2 || argc>3){std::cerr<<"usage: FHGProbe <port> [ipv6]\n";return 2;}
  int port=_wtoi(argv[1]);
  if(port<1 || port>65535)return 2;
  bool ipv6=argc==3 && std::wstring(argv[2])==L"ipv6";
  if(argc==3 && !ipv6)return 2;
  WSADATA d{};
  if(WSAStartup(MAKEWORD(2,2),&d)!=0)return 2;
  SOCKET s=socket(ipv6?AF_INET6:AF_INET,SOCK_STREAM,IPPROTO_TCP);
  if(s==INVALID_SOCKET){WSACleanup();return 2;}
  int rc=SOCKET_ERROR;
  if(ipv6) {
    sockaddr_in6 remote{};
    remote.sin6_family=AF_INET6;
    remote.sin6_addr=in6addr_loopback;
    remote.sin6_port=htons(static_cast<u_short>(port));
    rc=connect(s,reinterpret_cast<sockaddr*>(&remote),sizeof(remote));
  } else {
    sockaddr_in remote{};
    remote.sin_family=AF_INET;
    remote.sin_addr.s_addr=htonl(0x7f000001);
    remote.sin_port=htons(static_cast<u_short>(port));
    rc=connect(s,reinterpret_cast<sockaddr*>(&remote),sizeof(remote));
  }
  if(rc==0)std::cout<<"CONNECTED "<<(ipv6?"IPv6":"IPv4")<<" "<<port<<"\n";
  else std::cout<<"BLOCKED/FAILED "<<(ipv6?"IPv6":"IPv4")<<" "<<port<<" WSA="<<WSAGetLastError()<<"\n";
  closesocket(s);
  WSACleanup();
  return rc==0?0:1;
}
