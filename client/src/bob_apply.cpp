#define NOMINMAX
#include "bob_apply.h"
#include "anim.h"
#include "bob.h"
#include "cvar.h"
#include "input_hook.h"
#include "log.h"
#include "mem.h"
#include "state.h"
#include "telemetry.h"
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
namespace feel {
namespace {
struct Runtime {
 HANDLE thread=nullptr, stop=nullptr, instance=nullptr;
 FloatCvar x,y,z;
 float bx=0,by=0,bz=0;
 bool applied=false;
 BobApplySettings settings;
} rt;
bool Focused() {
 DWORD pid=0; GetWindowThreadProcessId(GetForegroundWindow(), &pid);
 return pid==GetCurrentProcessId();
}
void Restore() {
 if (!rt.applied) return;
 if(rt.x.valid()) *rt.x.value=rt.bx;
 if(rt.y.valid()) *rt.y.value=rt.by;
 if(rt.z.valid()) *rt.z.value=rt.bz;
 rt.applied=false;
}
void LoadSettings() {
 wchar_t path[MAX_PATH]{}; HMODULE self=nullptr;
 GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
  reinterpret_cast<LPCWSTR>(&LoadSettings), &self);
 if(!GetModuleFileNameW(self,path,MAX_PATH)) return;
 std::wstring full(path);
 full=full.substr(0,full.find_last_of(L"\\/"))+L"\\csgo_feel.ini";
 FILE* file=nullptr;
 if(_wfopen_s(&file,full.c_str(),L"rb")||!file) return;
 char line[512],key[80]; float value;
 while(fgets(line,sizeof(line),file)) {
  if(sscanf_s(line," %79[^= ] = %f",key,static_cast<unsigned>(sizeof(key)),&value)!=2||!std::isfinite(value)) continue;
  auto& s=rt.settings;
  if(!strcmp(key,"enabled")) s.enabled=value!=0;
  else if(!strcmp(key,"bob")) s.bob=value!=0;
  else if(!strcmp(key,"xscale")) s.xScale=std::clamp(value,0.0f,0.1f);
  else if(!strcmp(key,"yscale")) s.yScale=std::clamp(value,0.0f,0.1f);
  else if(!strcmp(key,"diagnostics")) s.diagnostics=value!=0;
  else if(!strcmp(key,"qieqiang")) s.qieqiang=value!=0;
  else if(!strcmp(key,"qieqiang_amount")) s.qieqiangAmount=std::clamp(value,0.0f,2.5f);
  else if(!strcmp(key,"qieqiang_time")) s.qieqiangTime=std::clamp(value,0.10f,2.0f);
  else if(!strcmp(key,"input_trigger")) s.inputTrigger=value!=0;
  else if(!strcmp(key,"wheel")) s.inputTrigger=value!=0; // 旧键名，保留兼容
  else if(!strcmp(key,"recoil")) s.recoil=value!=0;
  else if(!strcmp(key,"recoil_kick")) s.recoilKick=std::clamp(value,0.0f,1.5f);
  else if(!strcmp(key,"recoil_punch_gain")) s.punchGain=std::clamp(value,-3.0f,3.0f);
 }
 fclose(file);
}
DWORD WINAPI Worker(void*) {
 BobState state;
 DeployAnim deploy;
 RecoilSpring recoil;
 SwitchGate gate;
 Telemetry snap{};
 bool snapValid=false,active=rt.settings.enabled,keyWasDown=false,wasGrounded=false;
 bool serverSeen=false,fireSeeded=false;
 uint32_t lastSequence=0,lastFireSeq=0,lastServerSeq=0,seqAtLocal=0;
 float localAt=-1e9f;
 int confirmLog=0,cancelLog=0;
 uint64_t lastFrameMs=0,heartbeat=0,hookRetry=0;
 while(WaitForSingleObject(rt.stop,2)==WAIT_TIMEOUT) {
  const auto nowMs=GetTickCount64();
  const float now=static_cast<float>(nowMs)*0.001f;
  float dt=lastFrameMs&&nowMs>lastFrameMs?static_cast<float>(nowMs-lastFrameMs)*0.001f:0.0f;
  dt=std::min(dt,0.05f);
  lastFrameMs=nowMs;

  if(nowMs-heartbeat>=1000) {
   StateHeartbeat(kFlagLink|kFlagKeyInput|(InputHookActive()?kFlagWindowHook:0u));
   heartbeat=nowMs;
  }
  // 窗口过程可能被游戏重置（换图 / 改分辨率）。2 秒校验一次，没挂上就重试。
  if(nowMs-hookRetry>=2000) { hookRetry=nowMs; InputHookKeepAlive(); }
  bool key=Focused()&&(GetAsyncKeyState(VK_F8)&0x8000);
  if(key&&!keyWasDown) { active=!active; Restore(); BobReset(state); deploy.Reset(); recoil.Reset(); }
  keyWasDown=key;

  Telemetry cur{};
  if(!active||!Focused()||!ReadTelemetry(cur)||!cur.enabled) {
   Restore();BobReset(state);deploy.Reset();recoil.Reset();
   snapValid=false;lastSequence=0;fireSeeded=false;serverSeen=false;localAt=-1e9f;
   continue;
  }

  // 参数与触发源只在服务端发布新快照时更新；渲染是每帧做的（见下）。
  const bool fresh=!snapValid||cur.sequence!=lastSequence;
  if(fresh) { lastSequence=cur.sequence; snap=cur; snapValid=true; }

  const auto& s=rt.settings;
  bool recoilOn=s.recoil&&(snap.animFlags&kAnimShangyang)&&(snap.animFlags&kAnimViewmodelRecoil);
  const float recoilScale=snap.recoilScale>0.0f?std::clamp(snap.recoilScale,0.0f,3.0f):1.0f;

  // ---- 开火计数：只在快照推进时补发冲量（低 tick 下连发会被合并，按差值补齐）
  if(fresh&&recoilOn&&!(snap.punchFlags&kPunchLive)) {
   if(!fireSeeded) { fireSeeded=true; lastFireSeq=snap.fireSeq; }
   else if(snap.fireSeq!=lastFireSeq) {
    uint32_t shots=snap.fireSeq>lastFireSeq?snap.fireSeq-lastFireSeq:0;
    lastFireSeq=snap.fireSeq;
    if(shots>0&&shots<=16)
     for(uint32_t i=0;i<shots;++i) recoil.Kick(s.recoilKick*recoilScale);
   }
  }

  // ---- 切枪动画：本地按下沿立刻播，服务端事件只做回执 / 拾取兜底
  //
  // 这里是「同步」的关键。CS2 的起枪动画是客户端预测的，按下 3 那一帧就开始了；
  // 服务端要等 usercmd 上行 + item_equip 派发 + 下一个 tick 发布快照才会告诉我们，
  // 40~80ms 已经过去了。所以本地窗口消息必须是主触发，服务端只负责确认。
  const bool qqOn=s.qieqiang&&(snap.animFlags&kAnimQieqiang);
  if(qqOn) {
   const float amt=snap.qieqiangAmount>0.01f?snap.qieqiangAmount:s.qieqiangAmount;
   const float dur=snap.qieqiangTime>0.10f?snap.qieqiangTime:s.qieqiangTime;

   if(s.inputTrigger&&gate.OnLocal(InputSwitchSeq(),now)) {
    deploy.Start(now,amt,dur);
    localAt=now;seqAtLocal=snap.switchSeq;
   }

   if(!serverSeen) {
    serverSeen=true;lastServerSeq=snap.switchSeq;gate.OnServer(snap.switchSeq,now);
   }
   else if(snap.switchSeq!=lastServerSeq) {
    lastServerSeq=snap.switchSeq;
    if(gate.OnServer(snap.switchSeq,now)) {
     // 本地没按过：拾取 / 空枪自动切，这里补播
     deploy.Start(now,amt,dur);
     if(confirmLog<3) Log("切枪动画 服务端触发（seq=%u，本地无输入 → 拾取/自动切）",snap.switchSeq);
    }
    else if(localAt>-1e8f) {
     if(confirmLog<8||confirmLog%100==0)
      Log("切枪动画 本地→服务端确认 %dms（seq=%u）",static_cast<int>((now-localAt)*1000.0f),snap.switchSeq);
     ++confirmLog;
     localAt=-1e9f;
    }
   }

   // 误触撤销：本地按了但 200ms 内服务端没有对应换枪（聊天框打字、按了当前手持的枪位）。
   // 撤销后仍然刷新 localAt，让稍后到达的服务端回执继续被吞掉——否则一次误按会先播一下、
   // 再被回执补播第二遍。
   if(localAt>-1e8f&&now-localAt>kUnconfirmedCancel&&snap.switchSeq==seqAtLocal) {
    if(deploy.Playing(now)) {
     deploy.Abort(now);
     if(cancelLog<3) Log("切枪动画 撤销一次未确认的输入（服务端 200ms 内没有换枪）");
     ++cancelLog;
    }
    localAt=now;
   }

   if(!deploy.Playing(now)) deploy.Reset();
  }
  else {
   deploy.Reset();
   serverSeen=false;
   localAt=-1e9f;
  }

  // ---- 手臂晃动（服务端实测速度，客户端帧间隔积分）
  const bool grounded=(snap.flags&1u)!=0;
  if(s.bob&&snap.bob) {
   const float speed=std::hypot(snap.vx,snap.vy);
   const float yaw=snap.yaw*0.0174532925199433f;
   const float lateral=-std::sin(yaw)*snap.vx+std::cos(yaw)*snap.vy;
   const float strafe=speed>0.01f?std::clamp(lateral/speed,-1.0f,1.0f):0.0f;
   BobSettings bs;
   bs.lowerAmt=std::clamp(snap.lower,0.0f,30.0f);
   bs.amtLat=std::clamp(snap.lat,0.0f,2.0f);bs.amtVert=std::clamp(snap.vert,0.0f,2.0f);
   bs.bobCycle=std::clamp(snap.cycle,0.12f,2.0f);
   bs.shiftLeft=std::clamp(snap.shiftLeft,0.0f,3.0f);bs.shiftRight=std::clamp(snap.shiftRight,0.0f,3.0f);
   BobUpdate(state,bs,dt,grounded?speed:0.0f,strafe);
   if(grounded!=wasGrounded) BobReset(state);
   wasGrounded=grounded;
  } else {
   BobReset(state);
  }

  // ---- 枪口上扬：优先跟随引擎真实后坐角；没有就退回「开火计数 + 弹簧」
  float recoilOffset=0.0f;
  if(recoilOn) {
   if(snap.punchFlags&kPunchLive) {
    // 真实后坐角：引擎自己按 view_punch_decay 回落，客户端只缩放跟随，不用弹簧。
    recoil.Reset();
    fireSeeded=false;
    recoilOffset=PunchToOffset(snap.punchPitch,s.punchGain*recoilScale);
   } else {
    recoil.Update(dt,std::clamp(snap.punchDecay,5.0f,60.0f));
    recoilOffset=recoil.offset;
   }
  } else {
   recoil.Reset();
   fireSeeded=false;
  }

  // ---- 合成：x = 左右晃动 + 横移；z = 上下晃动 - 下沉 + 切枪 + 上扬
  const float bobZ=(state.vert-state.lower)*s.yScale;
  *rt.x.value=std::clamp(rt.bx+(state.lat+state.shift)*s.xScale,-2.0f,2.5f);
  *rt.y.value=rt.by; // Y is forward, NOT vertical.
  *rt.z.value=std::clamp(rt.bz+bobZ+deploy.Sample(now)+recoilOffset,-2.0f,2.0f);
  rt.applied=true;
 }
 Restore();InputHookStop();CloseTelemetry();StateClose();return 0;
}
}
bool BobApplyStart() {
 if(!strstr(GetCommandLineA(),"-insecure")) {Log("Refusing client: -insecure required");return false;}
 auto client=GetModuleInfo("client.dll");if(!client) return false;
 wchar_t name[80];swprintf_s(name,L"Local\\csgo_feel_client_%lu",GetCurrentProcessId());
 rt.instance=CreateMutexW(nullptr,FALSE,name);if(!rt.instance) return false;
 if(GetLastError()==ERROR_ALREADY_EXISTS) {CloseHandle(rt.instance);rt.instance=nullptr;return false;}
 bool ok=BindFloatCvar(client,"viewmodel_offset_x",0x2536920,rt.x)&&
  BindFloatCvar(client,"viewmodel_offset_y",0x2536930,rt.y)&&
  BindFloatCvar(client,"viewmodel_offset_z",0x2536940,rt.z);
 if(!ok) {CloseHandle(rt.instance);rt.instance=nullptr;Log("Offsets unavailable: no writes");return false;}
 rt.bx=*rt.x.value;rt.by=*rt.y.value;rt.bz=*rt.z.value;
 LoadSettings();StateOpen();
 // 滚轮 / 侧键读不到按键状态，只能靠窗口消息。装不上不致命：服务端那条链路还在。
 if(!InputHookStart()) Log("窗口消息接管未完成（窗口可能还没创建），2 秒后重试");
 rt.stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);
 // Worker code must not be unmapped by a FreeLibrary call under loader lock.
 HMODULE pinned=nullptr;
 GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
  reinterpret_cast<LPCWSTR>(&BobApplyStart), &pinned);
 if(rt.stop) rt.thread=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);
 if(!rt.thread) {if(rt.stop) CloseHandle(rt.stop);rt.stop=nullptr;CloseHandle(rt.instance);rt.instance=nullptr;StateClose();return false;}
 Log("Telemetry approximation; bob from real velocity. F8 toggles locally. qieqiang=%d input_trigger=%d recoil=%d",
  rt.settings.qieqiang?1:0, rt.settings.inputTrigger?1:0, rt.settings.recoil?1:0);
 return true;
}
void BobApplyStop() {
 if(!rt.thread) return;
 SetEvent(rt.stop);
 if(WaitForSingleObject(rt.thread,2000)!=WAIT_OBJECT_0) return;
 CloseHandle(rt.thread);CloseHandle(rt.stop);CloseHandle(rt.instance);
 rt.thread=rt.stop=rt.instance=nullptr;
}
void BobApplyRestore() {Restore();}
bool BobApplyDiagnostics() {return rt.settings.diagnostics;}
}
