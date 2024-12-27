using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.UI;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.YubicoOauthOTP
{
    public class Main : IPlugin, IContextMenu, IDisposable, ISettingProvider
    {
        public static string PluginID => "44AAE0133C0141D28208A5360318B2AB";
        public string Name => "yubico oath OTP";
        public string Description => "Generate codes from OATH accounts stored on the YubiKey.";
        private PluginInitContext Context { get; set; }
        private string IconPath { get; set; }
        private bool Disposed { get; set; }
        private string _cachedOutput;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(10);
        public Control CreateSettingPanel() => throw new NotImplementedException();
        public static string YkmanPath { get; set; } = "ykman"; // Default to "ykman" in $PATH

        public static string device { get; set; }
        public IEnumerable<PluginAdditionalOption> AdditionalOptions => [
             new()
                    {
                        Key = nameof(YkmanPath),
                        DisplayLabel = "ykman PATH",
                        DisplayDescription = "custom path for ykman, default to ykman in $PATH",
                        PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                        TextValue = YkmanPath,
                    },
             new()
                    {
                        Key = nameof(device),
                        DisplayLabel = "device",
                        DisplayDescription = "(Optional) specify which YubiKey to interact with by serial number\n" +
                 "     List connected YubiKeys, only output serial number:\r\n" +
                 "    $ ykman list --serials\n" +
                 "   Show information about YubiKey with serial number 123456:\r\n" +
                 "    $ ykman --device 123456 info",
                        PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                        TextValue = device,
                    },

        ];

        public List<Result> Query(Query query)
        {
            try
            {
                var accounts = GetAccounts();
                return FilterAccounts(accounts, query.Search)
                    .Select(account => CreateResult(account))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Query error: {ex.Message}");
                return
                [
                    new() {
                        Title = "Error",
                        SubTitle = ex.Message,
                        Action = _ =>
                        {
                            MessageBox.Show(ex.Message, "YubicoOauthOTP Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return true;
                        }
                    }
                ];
            }
        }

        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is string search)
            {
                return
                [
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy to clipboard (Ctrl+C)",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8C8", // Copy
                        AcceleratorKey = Key.C,
                        AcceleratorModifiers = ModifierKeys.Control,
                        Action = _ =>
                        {
                            Clipboard.SetDataObject(search);
                            return true;
                        },
                    }
                ];
            }

            return [];
        }
        private List<Account> GetAccounts()
        {
            var args = "";
            if (device != null)
            {
                args += $"--device {device} ";
            }
            args += "oath accounts code";

            return ParseYkmanOutput(RunCommand(YkmanPath, args));
        }

        private IEnumerable<Account> FilterAccounts(IEnumerable<Account> accounts, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return accounts;

            return accounts.Where(account => account.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        private Result CreateResult(Account account)
        {
            return new Result
            {
                Title = account.Name,
                SubTitle = $"Code: {account.Code}",
                Action = _ =>
                {
                    Clipboard.SetText(account.Code);
                    return true;
                }
            };
        }
        private string RunCommand(string fileName, string arguments)
        {
            if (_cachedOutput != null && DateTime.Now - _lastCacheUpdate < _cacheDuration)
            {
                Debug.WriteLine("Using cached result.");
                return _cachedOutput;
            }
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    process.Kill();
                    throw new TimeoutException("The command timed out.");
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Command failed with exit code {process.ExitCode}: {error}");
                }

                _cachedOutput = output;
                _lastCacheUpdate = DateTime.Now;

                return output;
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"Timeout: {ex.Message}");
                throw new Exception("The CLI command timed out. Please ensure the Yubico CLI is installed and accessible.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Command Error: {ex.Message}");
                throw new Exception($"Error running CLI command: {ex.Message}");
            }
        }

        private List<Account> ParseYkmanOutput(string output)
        {
            var accounts = new List<Account>();

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var code = parts.Last();
                    var name = string.Join(" ", parts.Take(parts.Length - 1));
                    accounts.Add(new Account { Name = name.Trim(), Code = code.Trim() });
                }
            }

            return accounts;
        }

        public class Account
        {
            public string Name { get; set; }
            public string Code { get; set; }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (Disposed || !disposing)
            {
                return;
            }

            if (Context?.API != null)
            {
                Context.API.ThemeChanged -= OnThemeChanged;
            }

            Disposed = true;
        }

        private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? "Images/yubicooauthotp.light.png" : "Images/yubicooauthotp.dark.png";

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            device = settings.AdditionalOptions
           .FirstOrDefault(x => x.Key == nameof(device))?.TextValue;

            var userProvidedPath = settings.AdditionalOptions
                .FirstOrDefault(x => x.Key == nameof(YkmanPath))?.TextValue;

            YkmanPath = !string.IsNullOrWhiteSpace(userProvidedPath)
                ? (Directory.Exists(userProvidedPath)
                    ? Path.Combine(userProvidedPath, "ykman")
                    : userProvidedPath)
                : "ykman";

            if (!File.Exists(YkmanPath))
            {
                YkmanPath = "ykman";
            }
        }
    }

}
