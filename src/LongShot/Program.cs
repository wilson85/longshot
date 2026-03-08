using LongShot;
using LongShot.App;

using var window = new GameWindow(1280, 720, "LongShot");
using var renderer = new DX12Renderer(window);
using var app = new LongShotApp(window, renderer);

app.Run();