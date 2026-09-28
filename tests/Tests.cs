using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using RemoteSupport;
class Tests {
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    static ReleaseAsset Asset(string suffix) { return new ReleaseAsset { name="rustdesk-1.4.9"+suffix, browser_download_url="https://github.com/rustdesk/rustdesk/releases/download/1.4.9/rustdesk-1.4.9"+suffix, digest="sha256:"+new string('a',64), size=4096 }; }
    static int Main() {
        try {
            char[] chars=SetupLogic.ConfigString().ToCharArray(); Array.Reverse(chars);
            string encoded=new string(chars).Replace('-','+').Replace('_','/');
            encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            var config=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            Check(config["host"]=="ds307.duckdns.org", "exact ID server");
            Check(config["relay"]=="", "empty relay server");
            Check(config["key"]=="" && config["api"]=="" && config.Count==4, "empty public key and API server");
            Check(SetupLogic.AssetSuffix("AMD64",true)=="-x86_64.exe", "Intel/AMD selection");
            Check(SetupLogic.AssetSuffix("ARM64",true)=="-aarch64.exe", "ARM64 selection");
            Reject(delegate { SetupLogic.AssetSuffix("x86",false); }, "unsupported 32-bit rejected");
            var x64=Asset("-x86_64.exe"); var arm=Asset("-aarch64.exe");
            var release=new Release { tag_name="1.4.9", assets=new[] {x64,arm} };
            Check(SetupLogic.SelectAsset(release,"-x86_64.exe")==x64, "correct release asset");
            release.prerelease=true; Reject(delegate {SetupLogic.SelectAsset(release,"-x86_64.exe");},"prerelease rejected"); release.prerelease=false;
            release.draft=true; Reject(delegate {SetupLogic.SelectAsset(release,"-x86_64.exe");},"draft rejected"); release.draft=false;
            x64.browser_download_url="https://example.com/rustdesk.exe"; Reject(delegate {SetupLogic.SelectAsset(release,"-x86_64.exe");},"unofficial download rejected");
            x64=Asset("-x86_64.exe"); release.assets=new[]{x64};
            x64.digest=null; Reject(delegate {SetupLogic.SelectAsset(release,"-x86_64.exe");},"missing checksum rejected");
            x64=Asset("-x86_64.exe"); release.assets=new[]{x64,x64}; Reject(delegate {SetupLogic.SelectAsset(release,"-x86_64.exe");},"ambiguous assets rejected");
            Check(SetupLogic.ParseVersion("v1.4.9")==SetupLogic.ParseVersion("1.4.9.0"),"normalized versions");
            Check(SetupLogic.ParseVersion("1.5.0")>SetupLogic.ParseVersion("1.4.9"),"newer version not downgraded");
            string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"checksum-test.bin");
            File.WriteAllText(file,"abc",new UTF8Encoding(false));
            Check(SetupLogic.Hash(file)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256 known vector");
            var verified=new ReleaseAsset {size=3,digest="sha256:"+SetupLogic.Hash(file)};
            SetupLogic.Verify(file,verified); Check(true,"matching download accepted");
            File.AppendAllText(file,"tampered"); Reject(delegate {SetupLogic.Verify(file,verified);},"tampered download rejected");
            var options=new Dictionary<string,string> { {"custom-rendezvous-server",SetupLogic.Server}, {"relay-server",SetupLogic.Relay}, {"key",""}, {"api-server",""} };
            Func<string,string> read=delegate(string option) {return options[option];};
            Check(SetupLogic.ConfigMatches(read,SetupLogic.Server,SetupLogic.Relay),"matching server settings accepted");
            options["key"]="stale-key";
            Check(!SetupLogic.ConfigMatches(read,SetupLogic.Server,SetupLogic.Relay),"stale public key fails read-back"); options["key"]="";
            options["relay-server"]="wrong.invalid";
            Check(!SetupLogic.ConfigMatches(read,SetupLogic.Server,SetupLogic.Relay),"stale relay fails read-back"); options["relay-server"]=SetupLogic.Relay;
            options["custom-rendezvous-server"]="";
            Check(!SetupLogic.ConfigMatches(read,SetupLogic.Server,SetupLogic.Relay),"missing ID server fails read-back"); options["custom-rendezvous-server"]=SetupLogic.Server;
            options["api-server"]="https://old.invalid";
            Check(!SetupLogic.ConfigMatches(read,SetupLogic.Server,SetupLogic.Relay),"stale API fails read-back");
            Check(SetupLogic.IsMissingService(new InvalidOperationException("wrapper",new System.ComponentModel.Win32Exception(1060))),"nested missing-service error recognized");
            Check(!SetupLogic.IsMissingService(new System.ComponentModel.Win32Exception(5)),"access denied is not treated as missing service");
            Check(!SetupLogic.IsMissingService(null),"no error does not trigger service repair");
            Console.WriteLine(count+" tests passed; no installation or settings were changed."); return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
