using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.YubicoOauthOTP.UnitTests
{
    [TestClass]
    public class MainTests
    {
        private Main main;

        [TestInitialize]
        public void TestInitialize()
        {
            main = new Main();
        }

        [TestMethod]
        public void Query_should_return_results()
        {
            var results = main.Query(new("search"));

            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count, "Query should return an empty list for a generic search.");
        }

        [TestMethod]
        public void LoadContextMenus_should_return_results()
        {
            var results = main.LoadContextMenus(new Result { ContextData = "search" });

            Assert.IsNotNull(results);
            Assert.AreEqual(1, results.Count, "LoadContextMenus should return a single context menu result.");
            Assert.AreEqual("Copy to clipboard (Ctrl+C)", results.First().Title);
        }

        [TestMethod]
        public void FilterAccounts_should_return_matching_accounts()
        {
            var accounts = new List<Main.Account>
            {
                new Main.Account { Name = "email@example.com", Code = "123456" },
                new Main.Account { Name = "work@example.com", Code = "654321" },
            };

            var results = main.FilterAccounts(accounts, "email").ToList();

            Assert.AreEqual(1, results.Count, "FilterAccounts should return one matching account.");
            Assert.AreEqual("email@example.com", results[0].Name);
        }

        [TestMethod]
        public void ParseYkmanOutput_should_parse_valid_output()
        {
            var output = @"
Activision:something@gmail.com                      111111
Amazon:email@private.com                                82934712
Avanza                                               036330
Bitwarden: email@private.com                        112310
something@gmail.com                                 610021
Discord:something@gmail.com                         103301
Dropbox                                              130310
Electronic Arts: something@gmail.com                033023
Epic Games                                     333101
Firefox: something@gmail.com                        100260
Github                                               631132
gitlab.com:gitlab.email@private.com   312163
Go Forum: email@private.com               013001
Google:email@private.com                  030033
Google privat                                        030060
Instagram                                            013013
LinkedIn: email@private.com               313023
Microsoft:email@work.se                  001126
Microsoft:something@gmail.com                       320063
npm                                               300001
NVIDIA: something@gmail.com                         301301
OpenAI:email@private.com                  331103
Plex                                                 661061
Reddit                                           306221
Rockstar+Games: something@gmail.com                 002331
SaltoSystems:SaltoSystems                            311333
srenity Vaultwarden:email@work.se        333103
SweClockers:                                      002031
10/Twitch                                           0333333
Twitter:@something                                  033303
Uber                                     060200
Ubiquiti SSO                                   302311
Ubisoft                                              006323
Vaultwarden:email@private.com             303333
";

            var accounts = main.ParseYkmanOutput(output);

            Assert.IsNotNull(accounts, "ParseYkmanOutput should not return null.");
            Assert.AreEqual(34, accounts.Count, "ParseYkmanOutput should parse two accounts.");
            Assert.AreEqual("Activision:something@gmail.com", accounts[0].Name);
            Assert.AreEqual("111111", accounts[0].Code);
        }

        [TestMethod]
        public void ParseYkmanOutput_should_handle_empty_output()
        {
            var output = "";
            var accounts = main.ParseYkmanOutput(output);

            Assert.IsNotNull(accounts, "ParseYkmanOutput should not return null.");
            Assert.AreEqual(0, accounts.Count, "ParseYkmanOutput should return an empty list for empty output.");
        }

        [TestMethod]
        public void RunCommand_should_throw_exception_on_invalid_command()
        {
            Assert.ThrowsException<System.Exception>(() => main.RunCommand("invalidCommand", ""));
        }
    }
}
