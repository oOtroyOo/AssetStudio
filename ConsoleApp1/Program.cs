using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetStudio;

namespace ConsoleApp1
{
    class Program
    {
        private static string testFile = @"G:\Works\Project\HappyYoYo\build\data.unity3d";
        static void Main(string[] args)
        {
            BundleFile bundle = new BundleFile(testFile);
            foreach (var node in bundle.DirectoryInfo)
            {
                Console.WriteLine(node.size.ToString().PadRight(11) + node.path);
            }
            Stream globalgamemanagers = bundle.OpenAsset("globalgamemanagers");
            Stream unity_builtin_extra = bundle.OpenAsset("Resources/unity_builtin_extra");
            bundle.Dispose();
            Console.ReadKey();
        }
    }
}
