using LongShot;
using LongShot.App;
using LongShot.Engine;

using var window = new GameWindow(1280, 720, "LongShot");
RetroAudio.Init();
using var renderer = new DX12Renderer(window);
using var app = new LongShotApp(window, renderer);

app.Run();