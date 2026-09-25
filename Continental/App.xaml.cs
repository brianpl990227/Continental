using Continental.Shared.Services;

namespace Continental
{
    public partial class App : Application
    {
        private readonly IGameHost _host;

        public App(IGameHost host)
        {
            _host = host;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage()) { Title = "Continental" };

            window.Stopped += (_, _) => _host.SaveNow();
            window.Destroying += (_, _) => _host.SaveNow();

            return window;
        }
    }
}
