using Evergine.Components.Graphics3D;
using Evergine.Mathematics;

namespace Longshot.Utils;

public static class CueBuilder
{
    public static Entity BuildTronCue(SceneManager manager)
    {
        var tronMaterial = manager.Managers.AssetSceneManager.Load<Material>(EvergineContent.Materials.DefaultMaterial);

        Entity cuePivot = new Entity("PlayerCueStick")
            .AddComponent(new Transform3D());

        float cueLength = 1.0f; // 1 meter long
        float cueThickness = 0.015f;

        Entity cueMesh = new Entity("CueMesh")
            .AddComponent(new Transform3D()
            {
                LocalPosition = new Vector3(0, 0, cueLength / 2f),

                LocalRotation = new Vector3(MathHelper.PiOver2, 0, 0),

                LocalScale = new Vector3(cueThickness, cueLength, cueThickness)
            })
            .AddComponent(new CylinderMesh() { Tessellation = 16 }) // 16 sides for a slightly retro look
            .AddComponent(new MaterialComponent() { Material = tronMaterial })
            .AddComponent(new MeshRenderer());

        cuePivot.AddChild(cueMesh);

        return cuePivot;
    }
}