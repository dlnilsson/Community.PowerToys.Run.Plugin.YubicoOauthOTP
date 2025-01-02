using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Svg;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wox.Plugin;
using Wox.Plugin.Logger;


namespace Community.PowerToys.Run.Plugin.YubicoOauthOTP
{
    // https://github.com/beemdevelopment/Aegis/blob/master/docs/iconpacks.md
    public class IconPack
    {
        [JsonPropertyName("uuid")]
        public string UUID { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public double Version { get; set; }

        [JsonPropertyName("icons")]
        public List<Icon> Icons { get; set; }
    }

    public class Icon
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("issuer")]
        public List<string> Issuer { get; set; }
    }

    public class Main : IPlugin, IContextMenu, IDisposable, ISettingProvider, IDelayedExecutionPlugin
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

        public static string Device { get; set; }

        private string CacheDirectory { get; set; }

        public IconPack IconPack { get; set; }

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
        {
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
                Key = nameof(Device),
                DisplayLabel = "device",
                DisplayDescription = "(Optional) specify which YubiKey to interact with by serial number\n" +
                                      "List connected YubiKeys, only output serial number:\r\n" +
                                      "$ ykman list --serials\n" +
                                      "Show information about YubiKey with serial number 123456:\r\n" +
                                      "$ ykman --device 123456 info",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = Device,
            }
        };

        public List<Result> Query(Query query)
        {
            List<Result> results = [];
            return results;
        }
        public List<Result> Query(Query query, bool delayedExecution)
        {
            try
            {
                var accounts = GetAccounts();
                return FilterAccounts(accounts, query.Search)
                    .Select(CreateResult)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Info($"Query error: {ex.Message}", GetType());
                return
                [
                    new() {
                        Title = "Error",
                        SubTitle = ex.Message,
                        Glyph = "\xE000", // error
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

            // c# is wierd.
            // load the Svg.dll we ship with with the plugin build directory, see .csproj.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name.Contains("Svg"))
                {
                    string pluginDirectory = Context.CurrentPluginMetadata.PluginDirectory;
                    string svgAssemblyPath = Path.Combine(pluginDirectory, "Svg.dll");
                    if (File.Exists(svgAssemblyPath))
                    {
                        return Assembly.LoadFrom(svgAssemblyPath);
                    }
                }
                return null;
            };

            CacheDirectory = Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache");
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }

            ProcessIconPack(Context.CurrentPluginMetadata.PluginDirectory);
        }

        private void ProcessIconPack(string pluginDirectory)
        {
            var packFilePath = Path.Combine(pluginDirectory, "pack.json");
            Action<string> processPackJson = (filePath) =>
            {
                try
                {
                    string jsonContent = File.ReadAllText(filePath);
                    IconPack = JsonSerializer.Deserialize<IconPack>(jsonContent);
                    Log.Info($"Processed pack.json: Name={IconPack.Name}, Version={IconPack.Version}, Icons Count={IconPack.Icons?.Count}", GetType());
                }
                catch (Exception ex)
                {
                    Log.Info($"Error processing pack.json: {ex.Message}", GetType());
                }
            };

            if (File.Exists(packFilePath))
            {
                processPackJson(packFilePath);
                return;
            }

            try
            {
                var zipFiles = Directory.GetFiles(pluginDirectory, "*.zip");
                foreach (var zipFilePath in zipFiles)
                {
                    using (var archive = System.IO.Compression.ZipFile.OpenRead(zipFilePath))
                    {
                        var packEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("pack.json", StringComparison.OrdinalIgnoreCase));

                        if (packEntry != null)
                        {
                            System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, pluginDirectory, true);
                            Log.Debug($"Extracted all contents of {zipFilePath} to {pluginDirectory}", GetType());
                            if (File.Exists(packFilePath))
                            {
                                processPackJson(packFilePath);
                            }
                            return;
                        }
                    }
                }
                Log.Debug("No valid pack.json found in any zip files.", GetType());
            }
            catch (Exception ex)
            {
                Log.Info($"Error during icon pack processing: {ex.Message}", GetType());
            }
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
            if (Device != null)
            {
                args += $"--device {Device} ";
            }
            args += "oath accounts code";

            return ParseYkmanOutput(RunCommand(YkmanPath, args));
        }

        public IEnumerable<Account> FilterAccounts(IEnumerable<Account> accounts, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return accounts;

            return accounts.Where(account => account.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        private void ConvertSvgToPng(string svgFilePath, string pngFilePath, int width = 48, int height = 48)
        {
            try
            {
                var svgDocument = SvgDocument.Open(svgFilePath);
                svgDocument.Width = width;
                svgDocument.Height = height;
                using var bitmap = svgDocument.Draw();
                bitmap.Save(pngFilePath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Log.Info($"Failed to convert {svgFilePath} to PNG: {ex.Message}", GetType());
                throw;
            }
        }

        private string NormalizeFilePath(string pluginDirectory, string relativeFilePath)
        {
            string normalizedPath = relativeFilePath.Replace("/", Path.DirectorySeparatorChar.ToString());
            return Path.Combine(pluginDirectory, normalizedPath);
        }

        public string FindIconForAccount(string accountName)
        {
            if (IconPack == null || IconPack?.Icons == null)
            {
                Log.Info("IconPack is null or does not contain icons. Using default icon.", GetType());
                return null;
            }

            string original = accountName;
            if (accountName.Contains(":"))
            {
                accountName = accountName.Split(':', 2)[0].Trim();
            }

            string sanitizedAccountName = Regex.Replace(accountName, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "", RegexOptions.IgnoreCase).Trim();
            string q = sanitizedAccountName;

            if (string.IsNullOrWhiteSpace(sanitizedAccountName))
            {
                q = original;
                return null;
            }

            var matchingIcon = IconPack.Icons.FirstOrDefault(icon =>
                icon.Issuer.Any(issuer =>
                    string.Equals(q, issuer, StringComparison.OrdinalIgnoreCase)));


            matchingIcon ??= IconPack.Icons.FirstOrDefault(icon =>
                icon.Issuer.Any(issuer =>
                    q.Contains(issuer, StringComparison.InvariantCultureIgnoreCase)));

            if (matchingIcon != null)
            {
                string cacheFilePath = Path.Combine(CacheDirectory, $"{Path.GetFileNameWithoutExtension(matchingIcon.Filename)}.png");

                if (File.Exists(cacheFilePath))
                {
                    return cacheFilePath;
                }

                string svgFilePath = NormalizeFilePath(
                      Context.CurrentPluginMetadata.PluginDirectory,
                      matchingIcon.Filename
                  );

                if (File.Exists(svgFilePath))
                {
                    try
                    {
                        ConvertSvgToPng(svgFilePath, cacheFilePath);
                        return cacheFilePath;
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"Error converting {svgFilePath} to PNG: {ex.Message}", GetType());
                    }
                }
            }
            return null;
        }

        private Result CreateResult(Account account)
        {
            string iconPath = FindIconForAccount(account.Name);

            return new Result
            {
                Title = account.Name,
                SubTitle = $"Code: {account.Code}",
                IcoPath = iconPath,
                Action = _ =>
                {
                    Clipboard.SetText(account.Code);
                    return true;
                }
            };
        }
        public string RunCommand(string fileName, string arguments)
        {
            if (_cachedOutput != null && DateTime.Now - _lastCacheUpdate < _cacheDuration)
            {
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
                Log.Debug($"ykman command timeout {ex.Message}", GetType());
                throw new Exception("The CLI command timed out. Please ensure the Yubico CLI is installed and accessible.");
            }
            catch (Exception ex)
            {
                Log.Debug($"ykman command error {ex.Message}", GetType());
                throw new Exception($"Error running CLI command: {fileName} {arguments} {ex.Message}");
            }
        }

        public List<Account> ParseYkmanOutput(string output)
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
            Device = settings.AdditionalOptions
           .FirstOrDefault(x => x.Key == nameof(Device))?.TextValue;

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
