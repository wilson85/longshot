using LongShot;
using LongShot.App;
using LongShot.Engine;
using LongShot.Rendering;

using var window = new GameWindow(1280, 720, "LongShot");
RetroAudio.Init();
using var renderer = new DX12Renderer(window.Handle, 1280, 720);
using var app = new LongShotApp(window, renderer);

app.Run();