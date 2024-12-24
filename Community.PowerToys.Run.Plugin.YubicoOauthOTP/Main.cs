using ManagedCommon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Windows.UI;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.YubicoOauthOTP
{
    /// <summary>
    /// Main class of this plugin that implement all used interfaces.
    /// </summary>
    public class Main : IPlugin, IContextMenu, IDisposable
    {
        /// <summary>
        /// ID of the plugin.
        /// </summary>
        public static string PluginID => "44AAE0133C0141D28208A5360318B2AB";

        /// <summary>
        /// Name of the plugin.
        /// </summary>
        public string Name => "yubico oath OTP";

        /// <summary>
        /// Description of the plugin.
        /// </summary>
        public string Description => "Generate codes from OATH accounts stored on the YubiKey.";

        private PluginInitContext Context { get; set; }

        private string IconPath { get; set; }

        private bool Disposed { get; set; }

        /// <summary>
        /// Return a filtered list, based on the given query.
        /// </summary>
        /// <param name="query">The query to filter the list.</param>
        /// <returns>A filtered list, can be empty when nothing was found.</returns>
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
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Error",
                        SubTitle = ex.Message,
                        Action = _ =>
                        {
                            MessageBox.Show(ex.Message, "YubicoOauthOTP Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return true;
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Initialize the plugin with the given <see cref="PluginInitContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="PluginInitContext"/> for this plugin.</param>
        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());
        }

        /// <summary>
        /// Return a list context menu entries for a given <see cref="Result"/> (shown at the right side of the result).
        /// </summary>
        /// <param name="selectedResult">The <see cref="Result"/> for the list with context menu entries.</param>
        /// <returns>A list context menu entries.</returns>
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
            string output = RunCommand("ykman", "oath accounts code");
            return ParseYkmanOutput(output);
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
            try
            {
                using (var process = new Process
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
                })
                {
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

                    return output;
                }
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

        /// <summary>
        /// Wrapper method for <see cref="Dispose()"/> that dispose additional objects and events form the plugin itself.
        /// </summary>
        /// <param name="disposing">Indicate that the plugin is disposed.</param>
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
    }
}
