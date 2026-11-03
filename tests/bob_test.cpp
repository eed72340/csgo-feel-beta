#define NOMINMAX
#include <windows.h>
#include "../client/src/bob.h"
#include "../client/src/telemetry.h"
#include <cmath>
#include <limits>
#include <cstdio>
int main() {
 feel::BobSettings settings;
 feel::BobState s;
 for(int i=0;i<1000;++i) feel::BobUpdate(s,settings,0.01f,0,0);
 if(s.phase!=0 || s.lat!=0 || s.vert!=0 || s.lower!=0) return 1;
 feel::BobUpdate(s,settings,0.01f,250,1);
 if(!(s.phase>0 && s.shift<0 && std::isfinite(s.lat))) return 2;
 float phase=s.phase;
 feel::BobUpdate(s,settings,std::numeric_limits<float>::quiet_NaN(),250,0);
 if(s.phase!=phase) return 3;
 feel::BobReset(s);
 if(s.phase || s.shift) return 4;
 auto file=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,128,L"Local\\csgo_feel_telemetry_v1");
 if(!file || GetLastError()==ERROR_ALREADY_EXISTS) return 5; // never overwrite a running server
 auto p=static_cast<feel::Telemetry*>(MapViewOfFile(file,FILE_MAP_WRITE,0,0,128));
 if(!p) return 6;
 feel::Telemetry result{};
 *p={}; p->magic=0x544C4546u;p->version=1;p->sequence=2;p->tick=GetTickCount64();p->alive=1;
 if(!feel::ReadTelemetry(result)) return 7;
 p->sequence=3;if(feel::ReadTelemetry(result)) return 8;
 p->sequence=4;p->tick=0;if(feel::ReadTelemetry(result)) return 9;
 p->tick=GetTickCount64();p->vx=std::numeric_limits<float>::quiet_NaN();
 if(feel::ReadTelemetry(result)) return 10;
 p->vx=0;p->cycle=std::numeric_limits<float>::quiet_NaN();
 if(feel::ReadTelemetry(result)) return 11;
 p->cycle=1;p->alive=0;if(feel::ReadTelemetry(result)) return 12;
 feel::CloseTelemetry();UnmapViewOfFile(p);CloseHandle(file);
 std::puts("PASS: bob idle/movement/invalid-dt/reset; telemetry layout/coherent/odd-sequence/stale/NaN/dead");
}
