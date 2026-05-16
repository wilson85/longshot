using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using static TorchSharp.torch.nn.functional;

namespace LongShot.Train;

/// <summary>
/// Tiny MLP that maps (cueX, cueZ, objX, objZ) to (sin_yaw, cos_yaw).
/// Two hidden layers, 64 units each, ReLU. Approximately 4.7k parameters - enough capacity
/// to fit the geometric oracle exactly, small enough to train in seconds on CPU.
/// </summary>
public sealed class PoolAimNet : Module<Tensor, Tensor>
{
    private readonly Linear fc1;
    private readonly Linear fc2;
    private readonly Linear fc3;

    public PoolAimNet() : base(nameof(PoolAimNet))
    {
        fc1 = Linear(DataGen.FeatureDim, 64);
        fc2 = Linear(64, 64);
        fc3 = Linear(64, DataGen.LabelDim);
        RegisterComponents();
    }

    public override Tensor forward(Tensor x)
    {
        var h = relu(fc1.forward(x));
        h = relu(fc2.forward(h));
        return fc3.forward(h);
    }
}
