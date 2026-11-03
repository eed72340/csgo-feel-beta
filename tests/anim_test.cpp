#define NOMINMAX
#include "../client/src/anim.h"
#include <cmath>
#include <cstdio>
#include <limits>

namespace
{
int g_fail = 0;

void Check(bool ok, const char* what)
{
    if (!ok)
    {
        std::printf("FAIL: %s\n", what);
        ++g_fail;
    }
}

bool Near(float a, float b, float eps = 1e-4f)
{
    return std::fabs(a - b) <= eps;
}
} // namespace

int main()
{
    using namespace feel;

    // ---- 切枪动画：静止时不产生位移
    {
        DeployAnim a;
        Check(a.Sample(10.0f) == 0.0f, "idle deploy samples zero");
        Check(!a.Playing(10.0f), "idle deploy not playing");
    }

    // ---- 起手不瞬移：t=0 必须严格在原位
    {
        DeployAnim a;
        a.Start(100.0f, 0.55f, 0.35f);
        Check(a.Playing(100.0f), "deploy playing right after start");
        Check(a.Sample(100.0f) == 0.0f, "deploy starts exactly at zero (no teleport)");
        Check(a.Sample(100.0875f) < 0.0f, "deploy dips after the start");
    }

    // ---- 曲线形状：最低点在 1/4 时长处、深度正好等于 amount
    {
        DeployAnim a;
        a.Start(0.0f, 0.5f, 0.4f);
        Check(Near(a.Sample(0.1f), -0.5f), "dip bottom equals amount at 0.25 duration");
        Check(Near(a.Sample(0.2f), 0.0f, 1e-3f), "crosses origin at 0.5 duration");
        Check(a.Sample(0.3f) > 0.0f, "small overshoot past origin near 0.75 duration");
        Check(Near(a.Sample(0.4f), 0.0f, 1e-4f), "returns to zero exactly at duration");
    }

    // ---- 收尾：duration 之后不再有台阶，1.25 倍后彻底停止
    {
        DeployAnim a;
        a.Start(0.0f, 0.5f, 0.4f);
        Check(Near(a.Sample(0.4f), 0.0f), "no step at the end of the curve");
        Check(a.Playing(0.4f * 1.2f), "tail still owned by baseline fade");
        Check(!a.Playing(0.4f * 1.3f), "deploy not playing after tail");
        Check(a.Sample(0.4f * 1.3f) == 0.0f, "nothing left after tail");
    }

    // ---- 连按切枪：重触发位置连续（这是「满屏乱跳」的根因之一）
    {
        DeployAnim a;
        a.Start(0.0f, 0.5f, 0.4f);
        float before = a.Sample(0.13f);
        a.Start(0.13f, 0.5f, 0.4f);
        float after = a.Sample(0.13f);
        Check(Near(before, after), "retrigger keeps position continuous");
        Check(before < -0.2f, "retrigger happens mid-dip in this case");
        // 连按三次也不应累积到超过单次深度
        a.Start(0.16f, 0.5f, 0.4f);
        a.Start(0.19f, 0.5f, 0.4f);
        bool bounded = true;
        for (float t = 0.19f; t < 0.6f; t += 0.01f)
            if (a.Sample(t) < -0.55f) bounded = false;
        Check(bounded, "spamming switches never exceeds the configured depth");
    }

    // ---- 误触撤销：从当前值平滑淡出，不产生台阶
    {
        DeployAnim a;
        a.Start(0.0f, 0.5f, 0.4f);
        float before = a.Sample(0.1f);
        a.Abort(0.1f);
        Check(Near(before, a.Sample(0.1f)), "abort keeps position continuous");
        Check(std::fabs(a.Sample(0.12f)) < std::fabs(before) * 0.5f, "abort fades out quickly");
        Check(a.Sample(0.25f) == 0.0f, "abort settles at zero");
    }

    // ---- 非法参数不改变状态；时长为负被夹到下限
    {
        DeployAnim a;
        a.Start(std::numeric_limits<float>::quiet_NaN(), 0.5f, 0.35f);
        Check(!a.Playing(1.0f), "NaN start ignored");
        a.Start(0.0f, 0.5f, -1.0f);
        Check(a.Playing(0.10f), "negative duration clamped to 0.10s floor");
    }

    // ---- 枪口上扬：冲量向上，弹簧回正
    {
        RecoilSpring r;
        Check(r.offset == 0.0f, "recoil starts at zero");
        r.Kick(0.30f);
        Check(r.velocity > 0.0f, "kick pushes upward (+z)");
        r.Update(0.02f, 18.0f);
        Check(r.offset > 0.0f, "recoil offset goes positive after kick");
        for (int i = 0; i < 400; ++i) r.Update(0.02f, 18.0f);
        Check(std::fabs(r.offset) < 1e-3f, "recoil settles back to zero");
    }

    // ---- 非法 dt / NaN 状态不炸，且能自愈
    {
        RecoilSpring r;
        r.Kick(0.2f);
        float before = r.offset;
        r.Update(std::numeric_limits<float>::quiet_NaN(), 18.0f);
        Check(r.offset == before, "NaN dt ignored");
        r.offset = std::numeric_limits<float>::quiet_NaN();
        r.Update(0.016f, 18.0f);
        Check(r.offset == 0.0f && r.velocity == 0.0f, "NaN state resets instead of propagating");
    }

    // ---- 本地优先：按下即播，服务端回执被吞掉，拾取才补播
    {
        SwitchGate g;
        Check(!g.OnLocal(1, 0.0f), "first local seq only seeds");
        Check(g.OnLocal(2, 1.0f), "local press plays immediately");
        Check(!g.OnServer(1, 1.05f), "server seed after local seed still seeds");
        Check(!g.OnServer(2, 1.06f), "server echo of the same press is swallowed");
        Check(g.OnServer(3, 2.00f), "server switch without local input plays (pickup)");
    }

    // ---- 快速连按：每次本地按下都播，服务端回执每条都被吞
    {
        SwitchGate g;
        g.OnLocal(1, 0.0f); // seed
        Check(g.OnLocal(2, 1.00f), "press 1 plays");
        Check(!g.OnServer(1, 1.05f), "echo 1 swallowed");
        Check(g.OnLocal(3, 1.15f), "press 2 plays");
        Check(!g.OnServer(2, 1.20f), "echo 2 swallowed");
        Check(g.OnLocal(4, 1.30f), "press 3 plays");
        Check(!g.OnServer(3, 1.35f), "echo 3 swallowed");
    }

    // ---- 服务端序号回退（插件热重载把计数器归零）不会被当成换枪
    {
        SwitchGate g;
        g.OnServer(5, 0.0f); // seed
        Check(g.OnServer(6, 1.0f), "plays before rollback");
        Check(!g.OnServer(0, 2.0f), "sequence rollback only realigns");
        Check(g.OnServer(1, 3.0f), "next switch after rollback plays");
    }

    // ---- 本地序号回退（DLL 重新注入）同样只对齐
    {
        SwitchGate g;
        g.OnLocal(5, 0.0f); // seed
        Check(g.OnLocal(6, 1.0f), "local press plays");
        Check(!g.OnLocal(0, 2.0f), "local rollback only realigns");
        Check(g.OnLocal(1, 3.0f), "next press after rollback plays");
    }

    // ---- Reset 后重新对齐，不会补播
    {
        SwitchGate g;
        g.OnServer(1, 0.0f);
        Check(g.OnServer(2, 1.0f), "plays before reset");
        g.Reset();
        Check(!g.OnServer(9, 2.0f), "after reset first seq only seeds");
    }

    // ---- 真实后坐角映射：方向、范围、脏数据
    {
        // 后坐把视角顶上去 → punch.X 为负 → 枪模要上抬（正偏移）
        Check(PunchToOffset(-2.0f, 0.35f) > 0.0f, "negative punch lifts the gun");
        Check(std::fabs(PunchToOffset(-2.0f, 0.35f) - 0.7f) < 1e-4f, "gain scales linearly");
        Check(PunchToOffset(2.0f, 0.35f) < 0.0f, "positive punch pushes the gun down");
        Check(PunchToOffset(-2.0f, -0.35f) < 0.0f, "negative gain flips the direction");
        Check(std::fabs(PunchToOffset(-40.0f, 1.0f)) < 1e-6f, "absurd punch ignored");
        Check(std::fabs(PunchToOffset(-20.0f, 3.0f) - 1.5f) < 1e-4f, "offset clamped to +1.5");
        Check(std::fabs(PunchToOffset(std::numeric_limits<float>::quiet_NaN(), 0.3f)) < 1e-6f,
              "NaN punch ignored");
    }

    if (g_fail == 0)
        std::puts("PASS: deploy curve (smooth in/out, retrigger, abort), recoil spring, "
                  "punch mapping, switch gate (local-first)");
    return g_fail == 0 ? 0 : 1;
}
