namespace CSGORestore;

/// <summary>影响手感的参数，全部与指令面板双向绑定。</summary>
public sealed class SimSettings
{
    public double Bob = 1;          // cl_bob
    public double NewBob = 0;       // cl_usenewbob
    public double LowerAmt = 21;    // cl_bob_lower_amt
    public double AmtLat = 0.33;    // local legacy default.cfg
    public double AmtVert = 0.14;
    public double BobCycle = 0.98;  // cl_bobcycle
    public double ShiftLeft = 1.5;  // cl_viewmodel_shift_left_amt
    public double ShiftRight = 0.75;
    public double Shangyang = 1;    // cs_go_shangyang
    public double ViewmodelRecoil = 1; // viewmodel_recoil
    public double PunchDecay = 18;  // view_punch_decay
    public double RecoilScale = 1;  // viewmodel_recoil_scale
    public double RunSpeed = 250;   // sim_run_speed
    public bool AutoWalk = true;    // sim_autowalk
}

/// <summary>
/// CS:GO 第一人称视角模型（viewmodel）晃动 + 枪口上扬的实时解算。
/// 单位说明：Bob* / Lower / Shift 是设计空间像素（1600x1000），Pitch 是角度。
/// </summary>
public sealed class Sim
{
    public SimSettings S { get; } = new();

    public double Speed { get; private set; }
    public double Strafe { get; private set; }
    public double BobLat { get; private set; }
    public double BobVert { get; private set; }
    public double Lower { get; private set; }
    public double Shift { get; private set; }
    public double Roll { get; private set; }
    public double IdleY { get; private set; }
    public double VmPitch { get; private set; }
    public double Punch { get; private set; }
    public double Flash { get; private set; }
    public double Distance { get; private set; }
    public double Time { get; private set; }
    public double Phase { get; private set; }
    public double WalkBlend { get; private set; }

    private double _vmVel;
    private double _pendingKick;
    private double _bobX;
    private double _bobY;

    public int ShotCount { get; private set; }

    public void Fire(double kickDeg)
    {
        _pendingKick += kickDeg;
        ShotCount++;
        Flash = 1.0;
    }

    public void Update(double dt, double moveForward, double moveStrafe, double speedScale = 1.0)
    {
        if (dt <= 0) return;
        dt = Math.Min(dt, 0.05);
        Time += dt;

        double targetSpeed;
        double targetStrafe;

        if (Math.Abs(moveForward) > 0.01 || Math.Abs(moveStrafe) > 0.01)
        {
            targetSpeed = S.RunSpeed * speedScale * (moveForward < 0 ? 0.55 : 1.0);
            targetStrafe = Math.Clamp(moveStrafe, -1, 1);
        }
        else if (S.AutoWalk)
        {
            // 自动演示：走走停停 + 左右横移，方便看清双频晃动的形状
            double t = Time * 0.9;
            targetSpeed = S.RunSpeed * (0.35 + 0.65 * Math.Abs(Math.Sin(t)));
            targetStrafe = Math.Sin(t * 0.55) * 0.9;
        }
        else
        {
            targetSpeed = 0;
            targetStrafe = 0;
        }

        double k = 1 - Math.Exp(-dt * 9);
        Speed += (targetSpeed - Speed) * k;
        Strafe += (targetStrafe - Strafe) * (1 - Math.Exp(-dt * 6));
        if (Speed < 0.4) Speed = 0;

        double s = Math.Clamp(Speed / 250.0, 0, 1.35);
        WalkBlend = Math.Clamp(Speed / 120.0, 0, 1);

        // ---- 晃动相位：速度越快、周期越短 ----
        double cyclesPerSecond = s / Math.Max(0.12, S.BobCycle);
        Phase += dt * Math.Tau * cyclesPerSecond;
        if (Phase > Math.Tau * 1000) Phase -= Math.Tau * 1000;

        double bob = S.Bob > 0.5 ? 1.0 : 0.0;

        double lat = Math.Sin(Phase) * S.AmtLat * s * 44 * bob;
        double vert = Math.Sin(Phase * 2 + Math.PI * 0.5) * S.AmtVert * s * 30 * bob;
        double lower = S.LowerAmt / 45.0 * s * s * 34 * bob;
        double roll = Math.Sin(Phase) * 0.35 * s * bob;

        if (S.NewBob > 0.5)
        {
            // CS2 的 new bob：更平滑、下沉更少，并带一点翻滚
            vert = Math.Sin(Phase * 2) * S.AmtVert * s * 20 * bob;
            lower *= 0.5;
            roll = Math.Sin(Phase) * 1.1 * s * bob;
        }

        // ---- 横移时枪模滞后 ----
        double shiftAmt = Strafe >= 0 ? S.ShiftRight : S.ShiftLeft;
        double shift = -Strafe * shiftAmt * 9 * WalkBlend * bob;

        // 平滑一下，避免一帧跳变
        double smooth = 1 - Math.Exp(-dt * 26);
        _bobX += (lat - _bobX) * smooth;
        _bobY += (vert - _bobY) * smooth;

        BobLat = _bobX;
        BobVert = _bobY;
        Lower = lower;
        Shift = shift;
        Roll = roll;

        // 静止时的呼吸感
        IdleY = Math.Sin(Time * 1.35) * 2.0 * (1 - WalkBlend);

        Distance += Speed * dt;

        // ---- 枪口上扬 / 视角后坐 ----
        if (_pendingKick > 0)
        {
            double kick = _pendingKick;
            _pendingKick = 0;

            if (S.Shangyang > 0.5)
            {
                if (S.ViewmodelRecoil > 0.5)
                    _vmVel += kick * 70.0 * S.RecoilScale;

                // 视角后坐始终存在（CS:GO 里 viewmodel_recoil 0 只是枪模不动）
                Punch += kick * 2.1 * S.RecoilScale;
                Punch = Math.Min(Punch, 40);
            }
        }

        // 视角上扬：按 view_punch_decay 指数回正
        Punch *= Math.Exp(-Math.Max(1.0, S.PunchDecay) * 0.95 * dt);

        // 枪模上扬：弹簧回正，回正速度同样受 view_punch_decay 影响
        const double stiffness = 320.0;
        double zeta = Math.Clamp(S.PunchDecay / 18.0, 0.3, 3.0);
        double damping = 2 * Math.Sqrt(stiffness) * zeta;
        _vmVel += (-stiffness * VmPitch - damping * _vmVel) * dt;
        VmPitch += _vmVel * dt;

        Flash = Math.Max(0, Flash - dt * 11);
    }

    public void Reset()
    {
        Speed = Strafe = BobLat = BobVert = Lower = Shift = Roll = IdleY = 0;
        VmPitch = Punch = Flash = Distance = 0;
        Phase = 0;
        _vmVel = 0;
        _pendingKick = 0;
        _bobX = _bobY = 0;
    }
}
