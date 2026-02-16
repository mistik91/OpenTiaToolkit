using System;
using System.EnterpriseServices;
using System.IO;

public class GacInstaller
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Installing TIA Portal V21 assemblies to GAC...");
        
        string basePath = "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\\";
        string[] assemblies = new string[] {
            basePath + "Siemens.Engineering.Base.dll",
            basePath + "Siemens.Engineering.Step7.dll",
            basePath + "Siemens.Engineering.WinCC.dll",
            basePath + "Siemens.Engineering.WinCC.Extension.dll"
        };
        
        System.EnterpriseServices.Internal.Publish publisher = new System.EnterpriseServices.Internal.Publish();
        
        foreach (string asmPath in assemblies)
        {
            if (!File.Exists(asmPath))
            {
                Console.WriteLine("File not found");
                continue;
            }
            
            string asmName = Path.GetFileName(asmPath);
            Console.Write("Installing...");
            
            try
            {
                publisher.GacInstall(asmPath);
                Console.WriteLine("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED");
            }
        }
        
        Console.WriteLine("Done!");
    }
}
using System;
using System.EnterpriseServices;
using System.IO;

public class GacInstaller
{
    public static void Main()
    {
        System.EnterpriseServices.Internal.Publish publisher = new System.EnterpriseServices.Internal.Publish();
        string basePath = "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\";
        string[] assemblies = new string[] {
            basePath + "Siemens.Engineering.Base.dll",
            basePath + "Siemens.Engineering.Step7.dll",
            basePath + "Siemens.Engineering.WinCC.dll",
            basePath + "Siemens.Engineering.WinCC.Extension.dll"
        };
        
        foreach (string asmPath in assemblies)
        {
            Console.Write("Installing " + System.IO.Path.GetFileName(asmPath) + "... ");
            try {
                publisher.GacInstall(asmPath);
                Console.WriteLine("OK");
            } catch {
                Console.WriteLine("FAILED");
            }
        }
    }
}
