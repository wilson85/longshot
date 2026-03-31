using Evergine.Common.Graphics;
using Evergine.Components.Cameras;
using Evergine.Components.Graphics3D;
using Evergine.Mathematics;
using Longshot.Gameplay.Match;

namespace Longshot.Gameplay;


public class MainSceneManager : SceneManager
{
    protected override void Start()
    {
        base.Start();

        EnsureManagersExist();
        EnsureCameraExists();
        EnsureLightExists();
        EnsureTableExists();
    }

    private void EnsureManagersExist()
    {
        if (this.Managers.FindManager<MatchSceneManager>() == null)
        {
            this.Managers.AddManager(new MatchSceneManager());
        }
    }

    private void EnsureCameraExists()
    {
        if (this.Managers.EntityManager.Find("MainCamera") == null)
        {
            var t3D = new Transform3D() { Position = new Vector3(0, 2, 3) };
            t3D.LookAt(Vector3.Zero);

            var ppgraph = new PostProcessingGraphRenderer()
            {
                ppGraph = this.Managers.AssetSceneManager.Load<PostProcessingGraph>(EvergineContent.PostProcessingGraphs.DefaultPostProcessingGraph)
            };

            Entity camera = new Entity("MainCamera")
                .AddComponent(t3D)
                .AddComponent(new Camera3D()
                {
                    FarPlane = 1000f
                })
                .AddComponent(new FreeCamera3D())
                .AddComponent(ppgraph);



            this.Managers.EntityManager.Add(camera);
        }
    }

    private void EnsureLightExists()
    {
        var entityManager = this.Managers.EntityManager;
        var lightEntity = entityManager.Find("TableLight");

        if (lightEntity == null)
        {
            lightEntity = new Entity("TableLight")
                .AddComponent(new Transform3D()
                {
                    LocalPosition = new Vector3(0, 1.8f, 0),
                    Rotation = new Vector3(MathHelper.ToRadians(90), 0, 0)
                })
                .AddComponent(new PhotometricRectangleAreaLight()
                {
                    Width = GameSettings.TableWidth * 2,
                    Height = GameSettings.TableWidth * 2,

                    Color = Color.FromHex("FF8585D3"),
                    LuminousPower = 400f,
                    IsShadowEnabled = true,
                });

            entityManager.Add(lightEntity);
        }
    }

    private void EnsureTableExists()
    {
        var entityManager = this.Managers.EntityManager;
        var tableEntity = entityManager.Find("Table");

        if (tableEntity == null)
        {
            tableEntity = new Entity("Table")
                .AddComponent(new Transform3D())
                .AddComponent(new TronTableDirector()
                {
                    // We assign the prefabs by loading them from the AssetsService
                    RailPrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseRail_weprefab),
                    SlatePrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseSlate_weprefab),
                    GatePrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseGate_weprefab),
                    BallPrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseBall_weprefab),
                });

            this.Managers.EntityManager.Add(tableEntity);
        }
    }
}
