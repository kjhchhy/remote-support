// Run only on a disposable GitHub Actions Windows runner, never on the user's PC.
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.ServiceProcess;
using System.Web.Script.Serialization;
using RemoteSupport;
class Integration {
    static void RemoveServiceForTest(string exe) {
        SetupLogic.RunCommand(exe,"--uninstall-service",60,false);
        var deadline=DateTime.UtcNow.AddSeconds(30);
        do {
            try { using(var service=new ServiceController("RustDesk")) { var status=service.Status; } }
            catch(InvalidOperationException error) { if(SetupLogic.IsMissingService(error)) return; throw; }
            Thread.Sleep(1000);
        } while(DateTime.UtcNow<deadline);
        throw new Exception("Test fixture failed to remove the service.");
    }
    static int Main() {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") {
            Console.Error.WriteLine("This installation test runs only on GitHub Actions."); return 2;
        }
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            Release release;
            string download=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"official-installer.exe");
            using(var client=SetupLogic.Client()) {
                release=new JavaScriptSerializer().Deserialize<Release>(client.DownloadString(SetupLogic.LatestUrl));
                var asset=SetupLogic.SelectAsset(release,"-x86_64.exe");
                client.DownloadFile(asset.browser_download_url,download);
                SetupLogic.Verify(download,asset);
            }
            SetupLogic.RunCommand(download,"--silent-install",240,false);
            var expected=SetupLogic.ParseVersion(release.tag_name);
            var deadline=DateTime.UtcNow.AddSeconds(120);
            string installed=null;
            do {
                installed=SetupLogic.InstalledExe();
                if(SetupLogic.IsCurrent(installed,expected)) break;
                Thread.Sleep(1500);
            } while(DateTime.UtcNow<deadline);
            if(!SetupLogic.IsCurrent(installed,expected)) throw new Exception("Installed version was not detected.");
            Console.WriteLine("PASS real installation: "+release.tag_name);
            // Regression: the packed download and unpacked installed binary legitimately differ.
            if(SetupLogic.Hash(download)==SetupLogic.Hash(installed)) throw new Exception("Expected distinct installer and unpacked binary for this regression fixture.");
            Console.WriteLine("PASS different installer/installed hashes do not block configuration");
            SetupLogic.EnsureService(installed,Console.WriteLine);
            const string testServer="support-test.invalid";
            SetupLogic.ApplyServerConfig(installed,testServer,testServer,Console.WriteLine);
            Console.WriteLine("PASS fresh installation configuration read-back");
            SetupLogic.RunCommand(installed,"--option key test-placeholder-public-key",30,false);
            SetupLogic.RunCommand(installed,"--option api-server https://old-server.invalid",30,false);
            SetupLogic.ApplyServerConfig(installed,testServer,testServer,Console.WriteLine);
            Console.WriteLine("PASS rerun clears stale key/API and verifies both server addresses");
            if(!SetupLogic.IsCurrent(installed,expected)) throw new Exception("Rerun version check failed.");
            Console.WriteLine("PASS current install is retained on rerun");
            RemoveServiceForTest(installed);
            if(!SetupLogic.IsCurrent(installed,expected)) throw new Exception("Missing-service fixture must retain current installed binary.");
            SetupLogic.ApplyServerConfig(installed,testServer,testServer,Console.WriteLine);
            Console.WriteLine("PASS latest version with missing service is repaired and configured");
            using(var service=new ServiceController("RustDesk")) {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));
            }
            SetupLogic.ApplyServerConfig(installed,testServer,testServer,Console.WriteLine);
            Console.WriteLine("PASS stopped service is started and settings verified");
            RemoveServiceForTest(installed);
            SetupLogic.RunCommand(download,"--silent-install",240,false);
            // A retained stop-service option can prevent service creation during reinstall.
            Thread.Sleep(10000);
            SetupLogic.ApplyServerConfig(installed,testServer,testServer,Console.WriteLine);
            Console.WriteLine("PASS reinstall with retained service-stop setting is repaired and configured");
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
