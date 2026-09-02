using System.Windows;

namespace FastOrder
{
    public partial class App : Application
    {
        public static string InstanceId { get; private set; } =
            string.Empty;

        public static bool HasExplicitInstance { get; private set; }

        protected override void OnStartup(
            StartupEventArgs e)
        {
            string instanceId =
                string.Empty;

            bool hasExplicitInstance =
                false;

            for (int index = 0;
                 index < e.Args.Length;
                 index++)
            {
                if (e.Args[index] != "--instance")
                {
                    continue;
                }

                if (hasExplicitInstance ||
                    index + 1 >= e.Args.Length ||
                    !IsValidInstanceId(
                        e.Args[index + 1]))
                {
                    MessageBox.Show(
                        "--instance requires one ID containing only A-Z, a-z, 0-9, _ or - (maximum 32 characters).",
                        "FastOrder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    Shutdown(
                        2);

                    return;
                }

                instanceId =
                    e.Args[++index];

                hasExplicitInstance =
                    true;
            }

            InstanceId =
                instanceId;

            HasExplicitInstance =
                hasExplicitInstance;

            base.OnStartup(
                e);
        }

        private static bool IsValidInstanceId(
            string value)
        {
            if (string.IsNullOrEmpty(
                    value) ||
                value.Length > 32)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool valid =
                    character >= 'A' && character <= 'Z' ||
                    character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '_' ||
                    character == '-';

                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
