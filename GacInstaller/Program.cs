using System;
using System.EnterpriseServices;

namespace GacInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Installing TIA Portal V21 assemblies to GAC...");

            System.EnterpriseServices.Internal.Publish publisher = new System.EnterpriseServices.Internal.Publish();
            string basePath = "C:\\Program Files\\Siemens\\Automation\\Portal V21\\PublicAPI\\V21\\net48\\";

            string[] assemblies = new string[]
            {
                basePath + "Siemens.Engineering.Base.dll",
                basePath + "Siemens.Engineering.Step7.dll",
                basePath + "Siemens.Engineering.WinCC.dll",
                basePath + "Siemens.Engineering.WinCC.Extension.dll"
            };

            foreach (string asmPath in assemblies)
            {
                string asmName = System.IO.Path.GetFileName(asmPath);
                Console.Write("Installing " + asmName + "... ");

                try
                {
                    publisher.GacInstall(asmPath);
                    Console.WriteLine("OK");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED: " + ex.Message);
                }
            }

            Console.WriteLine("\nDone!");
        }
    }
}
