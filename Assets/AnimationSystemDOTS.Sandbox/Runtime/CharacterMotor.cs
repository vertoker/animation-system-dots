using Unity.Mathematics;

namespace AnimationSystemDOTS.Sandbox
{
    public struct CharacterMotorState
    {
        public float3 Position;
        public float2 Velocity;
        public float VerticalSpeed, Yaw;
        public byte Grounded;
    }

    public struct CharacterMotorInput
    {
        public float2 Move;
        public float CameraYaw;
        public byte Sprint, Jump, Disabled, Strafe;
    }

    // Shared sample movement; no GameObject, input device or physics dependency.
    public static class CharacterMotor
    {
        public static void Step(ref CharacterMotorState state, in CharacterMotorInput input, float deltaTime)
        {
            if (!math.isfinite(deltaTime) || deltaTime <= 0) return;
            var move = input.Disabled == 0 && math.all(math.isfinite(input.Move)) ? input.Move : float2.zero;
            move /= math.max(1, math.length(move));
            var cameraYaw = math.isfinite(input.CameraYaw) ? input.CameraYaw : 0;
            math.sincos(cameraYaw, out var sine, out var cosine);
            var direction = new float2(cosine * move.x + sine * move.y, -sine * move.x + cosine * move.y);
            var desired = direction * (input.Sprint != 0 ? 6f : 3f);
            var difference = desired - state.Velocity;
            state.Velocity += difference * math.min(1, 15 * deltaTime / math.max(.001f, math.length(difference)));
            if (input.Strafe != 0) state.Yaw = cameraYaw;
            else if (math.lengthsq(direction) > .001f) state.Yaw = math.atan2(direction.x, direction.y);
            if (state.Grounded != 0 && input.Jump != 0 && input.Disabled == 0)
            { state.VerticalSpeed = 6; state.Grounded = 0; }
            state.VerticalSpeed -= 18 * deltaTime;
            state.Position += new float3(state.Velocity.x, state.VerticalSpeed, state.Velocity.y) * deltaTime;
            state.Position.xz = math.clamp(state.Position.xz, new float2(-24), new float2(24));
            if (state.Position.y <= 0)
            { state.Position.y = 0; state.VerticalSpeed = 0; state.Grounded = 1; }
        }
    }
}
