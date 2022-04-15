using Engine.AssetManagement;
using Engine.BackEnd;
using Game;
using Engine.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Engine.Utils.Logging;
using CommandLine;

namespace Game
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<CommandLineArgs>(args)
              .WithParsed(RunGame)
              .WithNotParsed(CommandLineError);
        }

        static void RunGame(CommandLineArgs commandLineArgs)
        {
            EngineConf.CreateInstance(commandLineArgs.Config);
            
            var virtualResolver = new VirtualFileAssetResolver();
            foreach (var item in EngineConf.GetInstance().GetMountLocations())
                virtualResolver.Mount(item.Key, item.Value);
            FileManager.Instance.RegisterResolver("asset", virtualResolver);
            FileManager.Instance.RegisterResolver("file", new FileAssetResolver());
            FileManager.Instance.RegisterResolver("memory", new MemoryAssetResolver());
            FileManager.Instance.RegisterResolver("web", new WebAssetResolver());

            Renderer renderer = new Renderer(new GameInstance());
        }

        static void CommandLineError(IEnumerable<Error> errs)
        {
            Console.WriteLine("Failed to parse command line arguments!");

            foreach (var item in errs)
            {
                Console.WriteLine(item.ToString());
            }
        }
    }
}
