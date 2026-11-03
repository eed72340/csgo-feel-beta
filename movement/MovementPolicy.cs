namespace CSGOFeel.Movement;

// Deterministic policy, independent of engine handles. This optional fast-stop
// mode is deliberately NOT described as CS:GO physics.
public static class MovementPolicy
{
    public static bool ShouldStop(bool alive, bool grounded, bool walkMovement,
        bool inputAvailable, bool movingInput, bool jumping, float vx, float vy)
        => alive && grounded && walkMovement && inputAvailable && !movingInput && !jumping &&
           float.IsFinite(vx) && float.IsFinite(vy) &&
           vx * vx + vy * vy > 0.0001f && vx * vx + vy * vy <= 350f * 350f;
}
