using CommandLine;
using CommandLine.Text;
using System;

namespace Game
{
    public class CommandLineArgs
    {
        [Option(
          Default = "config.json",
          HelpText = "The config file to load on application start")]
        public string Config{ get; set; }
    }
}
