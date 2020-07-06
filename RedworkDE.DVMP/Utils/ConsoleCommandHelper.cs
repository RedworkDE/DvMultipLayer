using System;
using System.Collections.Generic;
using System.Diagnostics;
using CommandTerminal;
using HarmonyLib;

namespace RedworkDE.DVMP.Utils
{
    /// <summary>
    /// Helper for adding console commands. Takes care of adding at the right time and adding autocomplete
    /// </summary>
    public static class ConsoleCommandHelper
    {
        private static List<Tuple<string, CommandInfo>>? _commands = new List<Tuple<string, CommandInfo>>();

        public static void RegisterCommand(string name, CommandInfo command)
        {
            _commands?.Add(Tuple.Create(name, command));
            if (_commands is null)
            {
                Terminal.Shell.AddCommand(name, command);
                Terminal.Autocomplete.Register(name);
            }
        }

        public static void RegisterCommand(string name, Action<CommandArg[]> func)
        {
            RegisterCommand(name, new CommandInfo() { proc = func, max_arg_count = -1 });
        }
        

        [HarmonyPatch(typeof(CommandShell), "RegisterCommands")]
        [HarmonyPrefix]
        public static void CommandShell_RegisterCommands_Patch()
        {
            if (!Terminal.Shell.Commands.ContainsKey("q")) Terminal.Shell.AddCommand("q", args => Process.GetCurrentProcess().Kill(), help: "Kill the game process");

            _commands?.ForEach(c => Terminal.Shell.AddCommand(c.Item1, c.Item2));
            _commands = null;
        }
    }

}