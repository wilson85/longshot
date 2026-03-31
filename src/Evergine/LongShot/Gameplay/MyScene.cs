using Evergine.Framework;

namespace Longshot.Gameplay;

public class MyScene : Scene
{
    public override void RegisterManagers()
    {
        base.RegisterManagers();
        this.Managers.AddManager(new MainSceneManager());
    }

    protected override void CreateScene()
    {

    }
}