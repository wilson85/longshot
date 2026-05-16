# LongShot.Train

End-to-end ML pipeline against the billiards engine using **TorchSharp** (Microsoft's .NET
binding to LibTorch, with a near-1:1 PyTorch API).

## Run

```pwsh
dotnet run --project src/LongShot.Train/LongShot.Train.csproj
```

First run downloads the LibTorch CPU binaries (~250 MB) via the `TorchSharp-cpu` NuGet.
Subsequent runs start in seconds.

End-to-end pipeline:
1. **Oracle baseline**: textbook ghost-ball aim run through the engine → reports the ceiling.
2. **Train**: mini-batch SGD on 100k synthetic (cue, obj) → (sin_yaw, cos_yaw) samples.
3. **Network eval**: same protocol with predicted aims.

Sample run (CPU, ~4 minutes):
```
Oracle:  26.8%
Network: 20.2%
Gap:     +6.6 pp
```

## The task

**Predict the aim direction that pockets a given object ball into the top-right corner pocket.**

- Input: `(cueX, cueZ, objX, objZ)` - both ball centres in table coordinates.
- Output: `(sin_yaw, cos_yaw)` - the aim direction's X and Z components. Two outputs (vs
  one yaw scalar) avoid the wrap-around discontinuity at ±π.
- Labels: deterministic ghost-ball geometry (object minus a ball-diameter along the line to
  the pocket, then aim from cue at that ghost).
- Network: 4 → 64 → 64 → 2 MLP, ReLU activations, ~4.7k params. Adam @ 1e-3.

## Files

- `Program.cs` - main entry point. Runs oracle, train, eval, prints a comparison.
- `DataGen.cs` - rejection-sampled random (cue, obj) positions; computes ghost-ball aim labels.
- `PoolAimNet.cs` - the model. `Module<Tensor, Tensor>` subclass, three Linear layers.
- `Trainer.cs` - mini-batch SGD loop, logs train/test MSE and mean angular error per epoch.
- `Evaluator.cs` - runs a freshly-built `BilliardsEngine` for each shot; counts pocketings
  via `OnBallPocketed`. Used by both the oracle baseline and the trained model.

## Why mean angular error matters more than MSE

For a target like `(sin(yaw), cos(yaw))`, MSE conflates magnitude error with angle error.
Two predictions with the same MSE can have very different angles. The trainer logs
`mean_ang_err_deg` as the more interpretable metric: at the typical corner-pocket distance
(~1.5 m), 1° of aim error = 17 mm of lateral miss at the pocket - inside an 80 mm pocket
mouth, so still pockets. 5° of error is a miss. The supervised network converges around
0.2° avg, which is fine on easy shots but loses precision on long/tight ones.

## Why the oracle isn't 100%

Random positions include physically marginal shots:
- Cue ball strikes the object too softly to overcome cushion/friction losses on the way to
  the pocket.
- Cue ball deflects off course due to **squirt** (the geometric formula doesn't compensate).
- The cue's path to the object crosses a rail (we don't filter these out).
- Object's path to the pocket grazes a rail jaw and bounces back.

The oracle's 26.8% is the **upper bound** the supervised network is reaching for.

## Next steps (RL territory)

This pipeline validates the TorchSharp + .NET training stack. To improve past the oracle
(i.e. actually solve harder shots), the next experiments are:

1. **Engine-validated labels**: for each (cue, obj), search aims (and maybe English offsets
   and forces) for one that actually pockets in the engine. Train against those. This
   teaches the net to compensate for squirt and physics imperfections.
2. **Add force + English to outputs**: 4 inputs → 5 outputs (sin_yaw, cos_yaw, force,
   offset_x, offset_y). With engine-validated labels, the net can learn when to use spin.
3. **Multiple pockets / multi-ball state**: extend the input to all 16 ball positions plus
   a "target pocket" one-hot. The network picks the best shot given the board.
4. **RL / self-play**: wrap `BilliardsEngine` in a `PoolEnv` (Gym-style Reset/Step/Reward),
   build a `LongShot.RL` project, train PPO. The engine's `SnapshotState`/`RestoreState`/
   `Clone` methods are there specifically to support parallel rollouts and tree search.

## GPU

Replace `TorchSharp-cpu` with `TorchSharp-cuda-12.1` in `LongShot.Train.csproj` and change
`var device = torch.CPU;` to `var device = torch.CUDA;` in `Trainer.cs`. Requires NVIDIA
GPU with matching CUDA toolkit installed.

Or: replace with `TorchSharp-cuda-11.7` for older toolkits.
