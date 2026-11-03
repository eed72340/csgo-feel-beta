using CounterStrikeSharp.API.Core;

namespace CSGOFeel;

/// <summary>
/// 读取引擎真实的 aim punch（后坐角）。
///
/// 为什么单独一层：`CCSPlayerPawn` 自己没有 `AimPunchAngle` 属性（编译期就没有），
/// 后坐角挂在组件上——`CCSPlayerPawn.AimPunchServices.PredictableBaseAngle`。
/// 拿到的是「本 tick 加到视角上的那一份」，也就是 CS:GO 里 viewmodel_recoil 跟随的东西，
/// 不需要按武器表猜角度，也不需要读原始的 schema 偏移。
///
/// 组件可能还没创建（刚连入 / 刚重生 / 正在切图），所以调用方必须允许失败，
/// 失败时退回「开火计数驱动的固定 kick」。
/// </summary>
internal static class AimPunchTap
{
    public static bool TryRead(CCSPlayerController player, out float pitch, out float yaw)
    {
        pitch = 0.0f;
        yaw = 0.0f;

        try
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return false;

            var services = pawn.AimPunchServices;
            if (services == null || services.Handle == IntPtr.Zero) return false;

            var angle = services.PredictableBaseAngle;
            if (!float.IsFinite(angle.X) || !float.IsFinite(angle.Y)) return false;

            // 离谱值直接丢掉：宁可这一帧不用 punch，也不要把枪模推飞。
            if (Math.Abs(angle.X) > 45.0f || Math.Abs(angle.Y) > 45.0f) return false;

            pitch = angle.X;
            yaw = angle.Y;
            return true;
        }
        catch
        {
            // 实体悬空 / 组件未就绪时直接异常，这里当作「本帧没有数据」。
            return false;
        }
    }
}
