using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            main.IconPack = new IconPack
            {
                Icons = new List<Icon>
            {
                new Icon
                {
                    Filename = "icons/1_Primary/Epic Games.svg",
                    Category = "1. Apps & Sites",
                    Issuer = new List<string> { "Epic Games" }
                },
                new Icon
                {
                    Filename = "icons/1_Primary/Dropbox.svg",
                    Category = "1. Apps & Sites",
                    Issuer = new List<string> { "Dropbox" }
                },
                new Icon
                {
                    Filename = "icons/4_Outdated/Bitwarden v1.svg",
                    Category = "4. Outdated",
                    Issuer = new List<string> { "Bitwarden", "Vaultwarden" }
                }
            }
            };
        }


        [TestMethod]
        public void Query_ShouldReturnNoResultsIfIconPackIsNull()
        {
            main.IconPack = null;

            var results = main.Query(new Query("Epic Games"));

            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count, "Query should return no results if IconPack is null.");
        }

        [TestMethod]
        public void ParseYkmanOutput_ShouldHandleMultipleAccounts()
        {
            var output = "Epic Games 123456\nDropbox 654321";

            var accounts = main.ParseYkmanOutput(output);

            Assert.IsNotNull(accounts, "ParseYkmanOutput should not return null.");
            Assert.AreEqual(2, accounts.Count, "ParseYkmanOutput should parse multiple accounts.");
            Assert.AreEqual("Epic Games", accounts[0].Name);
            Assert.AreEqual("123456", accounts[0].Code);
        }
    }
}