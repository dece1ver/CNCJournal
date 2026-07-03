using System;

namespace remeLog.Infrastructure.Types
{
    [Flags]
    public enum RemeLogFeature
    {
        None = 0,
        Ai = 1 << 0,
        AdvancedEdit = 1 << 1,
        Instances = 1 << 2,
    }
}
