/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Xml;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
#if NET_4_0
using System.Threading.Tasks;
#endif
using log4net;
using Nini.Config;
using log4net.Core;

namespace OpenSim.Framework
{
    public class Commands
    {
        public static bool _ConsoleIsCaseSensitive = true;

        /// <summary>
        /// Encapsulates a command that can be invoked from the console
        /// </summary>
        private class CommandInfo
        {
            /// <summary>
            /// The command for this commandinfo
            /// </summary>
            public string command;
            
            /// <summary>
            /// The help info for how to use this command
            /// </summary>
            public string commandHelp;

            /// <summary>
            /// Any info about this command
            /// </summary>
            public string info;

            /// <value>
            /// The method to invoke for this command
            /// </value>
            public List<CommandDelegate> fn;
        }

        private class CommandSet
        {
            private Dictionary<string, CommandInfo> commands = new Dictionary<string, CommandInfo> ();
            private Dictionary<string, CommandSet> commandsets = new Dictionary<string, CommandSet> ();
            private string ourPath = "";
            public string Path = "";
            private bool m_allowSubSets = true;
            
            public void Initialize (string path, bool allowSubSets)
            {
                m_allowSubSets = allowSubSets;
                ourPath = path;
                string[] paths = path.Split (' ');
                if (paths.Length != 0)
                    Path = paths[paths.Length - 1];
            }

            public void AddCommand (CommandInfo info)
            {
                if (!_ConsoleIsCaseSensitive) //Force to all lowercase
                    info.command = info.command.ToLower ();

                //If our path is "", we can't replace, otherwise we just get ""
                string innerPath = info.command;
                if (ourPath != "")
                    innerPath = info.command.Replace (ourPath, "");
                if (innerPath.StartsWith (" "))
                    innerPath = innerPath.Remove (0, 1);
                string[] commandPath = innerPath.Split (new string[1]{" "}, StringSplitOptions.RemoveEmptyEntries);
                if (commandPath.Length == 1 || !m_allowSubSets)
                {
                    //Only one command after our path, its ours

                    //Add commands together if there is more than one event hooked to one command
                    if (commands.ContainsKey (info.command))
                        commands[info.command].fn.AddRange (info.fn);
                    else
                        commands[info.command] = info;
                }
                else
                {
                    //Its down the tree somewhere
                    CommandSet downTheTree;
                    if (!commandsets.TryGetValue (commandPath[0], out downTheTree))
                    {
                        //Need to add it to the tree then
                        downTheTree = new CommandSet ();
                        downTheTree.Initialize ((ourPath == "" ? "" : ourPath + " ") + commandPath[0], false);
                        commandsets.Add (commandPath[0], downTheTree);
                    }
                    downTheTree.AddCommand (info);
                }
            }

            public string[] ExecuteCommand (string[] command)
            {
                if (command.Length != 0)
                {
                    string innerPath = string.Join (" ", command);
                    if (!_ConsoleIsCaseSensitive)
                        innerPath = innerPath.ToLower ();
                    if (ourPath != "")
                        innerPath = innerPath.Replace (ourPath, "");
                    if (innerPath.StartsWith (" "))
                        innerPath = innerPath.Remove (0, 1);
                    string[] commandPath = innerPath.Split (new string[1] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    if (commandPath.Length == 1 || !m_allowSubSets)
                    {
                        for (int i = 1; i <= command.Length; i++)
                        {
                            string[] comm = new string[i];
                            Array.Copy (command, comm, i);
                            string com = string.Join (" ", comm);
                            //Only one command after our path, its ours
                            if (commands.ContainsKey (com))
                            {
                                foreach (CommandDelegate fn in commands[com].fn)
                                {
                                    if (fn != null)
                                        fn (command);
                                }
                                return new string[0];
                            }
                            else if (commandPath[0] == "help")
                            {
                                List<string> help = GetHelp ();

                                foreach (string s in help)
                                    MainConsole.Instance.Output (s, Level.Severe);
                                return new string[0];
                            }
                            else
                            {
                                foreach (KeyValuePair<string, CommandInfo> cmd in commands)
                                {
                                    //See whether the command is the same length first
                                    string[] cmdSplit = cmd.Key.Split (' ');
                                    if (cmdSplit.Length == command.Length)
                                    {
                                        bool same = true;
                                        //Now go through each param and see whether they match up
                                        for (int k = 0; k < command.Length; k++)
                                        {
                                            if (!cmdSplit[k].StartsWith (command[k]))
                                            {
                                                same = false;
                                                break;
                                            }
                                        }
                                        //They do, execute it
                                        if (same)
                                        {
                                            foreach (CommandDelegate fn in cmd.Value.fn)
                                            {
                                                if (fn != null)
                                                    fn (command);
                                            }
                                            return new string[0];
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        string cmdToExecute = commandPath[0];
                        if (cmdToExecute == "help")
                            cmdToExecute = commandPath[1];
                        if (!_ConsoleIsCaseSensitive)
                            cmdToExecute = cmdToExecute.ToLower ();
                        //Its down the tree somewhere
                        CommandSet downTheTree;
                        if (commandsets.TryGetValue(cmdToExecute, out downTheTree))
                        {
                            return downTheTree.ExecuteCommand (commandPath);
                        }
                        else
                        {
                            //See if this is part of a word, and if it is part of a word, execute it
                            foreach (KeyValuePair<string, CommandSet> cmd in commandsets)
                            {
                                //If it starts with it, execute it (eg. q for quit)
                                if (cmd.Key.StartsWith (commandPath[0]))
                                {
                                    return cmd.Value.ExecuteCommand (commandPath);
                                }
                            }
                            if (commands.ContainsKey (cmdToExecute))
                            {
                                foreach (CommandDelegate fn in commands[cmdToExecute].fn)
                                {
                                    if (fn != null)
                                        fn (command);
                                }
                                return new string[0];
                            }
                        }
                    }
                }
                
                return new string[0];
            }

            public string[] FindCommands (string[] command)
            {
                List<string> values = new List<string> ();
                if (command.Length != 0)
                {
                    string innerPath = string.Join (" ", command);
                    if (!_ConsoleIsCaseSensitive)
                        innerPath = innerPath.ToLower ();
                    if (ourPath != "")
                        innerPath = innerPath.Replace (ourPath, "");
                    if (innerPath.StartsWith (" "))
                        innerPath = innerPath.Remove (0, 1);
                    string[] commandPath = innerPath.Split (new string[1] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    if ((commandPath.Length == 1 || !m_allowSubSets))
                    {
                        string fullcommand = string.Join(" ", command, 0, 2 > command.Length ? command.Length : 2);
                        foreach (KeyValuePair<string, CommandInfo> cmd in commands)
                        {
                            //If it starts with it, execute it (eg. q for quit)
                            if (cmd.Key.StartsWith (fullcommand))
                            {
                                values.Add (cmd.Value.commandHelp);
                            }
                        }
                        if (commandPath.Length != 0)
                        {
                            string cmdToExecute = commandPath[0];
                            if (cmdToExecute == "help")
                                cmdToExecute = commandPath[1];
                            if (!_ConsoleIsCaseSensitive)
                                cmdToExecute = cmdToExecute.ToLower ();
                            CommandSet downTheTree;
                            if (commandsets.TryGetValue (cmdToExecute, out downTheTree))
                            {
                                values.AddRange(downTheTree.FindCommands (commandPath));
                            }
                            else
                            {
                                //See if this is part of a word, and if it is part of a word, execute it
                                foreach (KeyValuePair<string, CommandSet> cmd in commandsets)
                                {
                                    //If it starts with it, execute it (eg. q for quit)
                                    if (cmd.Key.StartsWith (cmdToExecute))
                                    {
                                        values.AddRange (cmd.Value.FindCommands (commandPath));
                                    }
                                }
                            }
                        }
                    }
                    else if(commandPath.Length != 0)
                    {
                        string cmdToExecute = commandPath[0];
                        if (cmdToExecute == "help")
                            cmdToExecute = commandPath[1];
                        if (!_ConsoleIsCaseSensitive)
                            cmdToExecute = cmdToExecute.ToLower ();
                        //Its down the tree somewhere
                        CommandSet downTheTree;
                        if (commandsets.TryGetValue (cmdToExecute, out downTheTree))
                        {
                            return downTheTree.FindCommands (commandPath);
                        }
                        else
                        {
                            //See if this is part of a word, and if it is part of a word, execute it
                            foreach (KeyValuePair<string, CommandSet> cmd in commandsets)
                            {
                                //If it starts with it, execute it (eg. q for quit)
                                if (cmd.Key.StartsWith (cmdToExecute))
                                {
                                    return cmd.Value.FindCommands (commandPath);
                                }
                            }
                        }
                    }
                }

                return values.ToArray();
            }

            public List<string> GetHelp ()
            {
                List<string> help = new List<string> ();
                if (commandsets.Count != 0)
                {
                    help.Add ("");
                    help.Add ("------- Help Sets (type the name and help to get more info about that set) -------");
                    help.Add ("");
                }
                foreach (CommandSet set in commandsets.Values)
                {
                    help.Add (string.Format ("-- Help Set: {0}", set.Path));
                }
                if (help.Count != 0)
                {
                    help.Add ("");
                    help.Add ("------- Help options -------");
                    help.Add ("");
                }
                foreach (CommandInfo command in commands.Values)
                {
                    help.Add (string.Format ("-- {0}  [{1}]:   {2}", command.command, command.commandHelp, command.info));
                }
                return help;
            }

        }

        /// <value>
        /// Commands organized by keyword in a tree
        /// </value>
        private CommandSet tree = new CommandSet();

        /// <summary>
        /// Get help for the given help string
        /// </summary>
        /// <param name="helpParts">Parsed parts of the help string.  If empty then general help is returned.</param>
        /// <returns></returns>
        public List<string> GetHelp (string[] cmd)
        {
            List<string> help = new List<string> ();
            List<string> helpParts = new List<string> (cmd);

            // Remove initial help keyword
            helpParts.RemoveAt (0);

            help.AddRange (CollectHelp (helpParts));

            return help;
        }

        /// <summary>
        /// See if we can find the requested command in order to display longer help
        /// </summary>
        /// <param name="helpParts"></param>
        /// <returns></returns>
        private List<string> CollectHelp (List<string> helpParts)
        {
            return tree.GetHelp ();
        }

        /// <summary>
        /// Add a command to those which can be invoked from the console.
        /// </summary>
        /// <param name="command">The string that will make the command execute</param>
        /// <param name="commandHelp">The message that will show the user how to use the command</param>
        /// <param name="info">Any information about how the command works or what it does</param>
        /// <param name="fn"></param>
        public void AddCommand (string command, string commandHelp, string infomessage, CommandDelegate fn)
        {
            CommandInfo info = new CommandInfo ();
            info.command = command;
            info.commandHelp = commandHelp;
            info.info = infomessage;
            info.fn = new List<CommandDelegate> ();
            info.fn.Add (fn);
            tree.AddCommand (info);
        }

        public string[] FindNextOption (string[] cmd)
        {
            return tree.FindCommands (cmd);
        }

        public string[] Resolve (string[] cmd)
        {
            return tree.ExecuteCommand (cmd);
        }
    }
    public delegate void CommandDelegate(string[] cmd);

    public class Parser
    {
        public static string[] Parse(string text)
        {
            List<string> result = new List<string>();

            int index;

            string[] unquoted = text.Split(new char[] {'"'});

            for (index = 0 ; index < unquoted.Length ; index++)
            {
                if (index % 2 == 0)
                {
                    string[] words = unquoted[index].Split(new char[] {' '});
                    foreach (string w in words)
                    {
                        if (w != String.Empty)
                            result.Add(w);
                    }
                }
                else
                {
                    result.Add(unquoted[index]);
                }
            }

            return result.ToArray();
        }
    }

    /// <summary>
    /// A console that processes commands internally
    /// </summary>
    public class CommandConsole : ICommandConsole
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public bool m_isPrompting = false;
        public int m_lastSetPromptOption = 0;
        public List<string> m_promptOptions = new List<string> ();

        public virtual void Initialize(IConfigSource source, ISimulationBase baseOpenSim)
        {
            if (source.Configs["Console"] != null)
            {
                if (source.Configs["Console"].GetString("Console", String.Empty) != Name)
                    return;
            }
            else
                return;

            baseOpenSim.ApplicationRegistry.RegisterModuleInterface<ICommandConsole>(this);

            m_Commands.AddCommand ("help", "help",
                    "Get a general command list", Help);
        }

        public void Help(string[] cmd)
        {
            List<string> help = m_Commands.GetHelp(cmd);

            foreach (string s in help)
                Output(s, Level.Severe);
        }

        /// <summary>
        /// Display a command prompt on the console and wait for user input
        /// </summary>
        public void Prompt()
        {
            // Set this culture for the thread 
            // to en-US to avoid number parsing issues
            OpenSim.Framework.Culture.SetCurrentCulture();
            string line = ReadLine(m_defaultPrompt + "# ", true, true);

            if (line != String.Empty && line.Replace(" ", "") != String.Empty) //If there is a space, its fine
            {
                m_log.Info("[CONSOLE] Invalid command");
            }
        }

        public void RunCommand(string cmd)
        {
            string[] parts = Parser.Parse(cmd);
            m_Commands.Resolve(parts);
            Output("");
        }

        public virtual string ReadLine(string p, bool isCommand, bool e)
        {
            string oldDefaultPrompt = m_defaultPrompt;
            m_defaultPrompt = p;
            System.Console.Write("{0}", p);
            string cmdinput = System.Console.ReadLine();

            if (isCommand)
            {
                string[] cmd = m_Commands.Resolve(Parser.Parse(cmdinput));

                if (cmd.Length != 0)
                {
                    int i;

                    for (i=0 ; i < cmd.Length ; i++)
                    {
                        if (cmd[i].Contains(" "))
                            cmd[i] = "\"" + cmd[i] + "\"";
                    }
                    return String.Empty;
                }
            }
            m_defaultPrompt = oldDefaultPrompt;
            return cmdinput;
        }

        public string CmdPrompt(string p)
        {
            m_isPrompting = true;
            string line = ReadLine(String.Format("{0}: ", p), false, true);
            m_isPrompting = false;
            return line;
        }

        public string CmdPrompt(string p, string def)
        {
            m_isPrompting = true;
            string ret = ReadLine(String.Format("{0} [{1}]: ", p, def), false, true);
            if (ret == String.Empty)
                ret = def;

            m_isPrompting = false;
            return ret;
        }

        public string CmdPrompt(string p, List<char> excludedCharacters)
        {
            m_isPrompting = true;

            bool itisdone = false;
            string ret = String.Empty;
            while (!itisdone)
            {
                itisdone = true;
                ret = CmdPrompt(p);

                foreach (char c in excludedCharacters)
                {
                    if (ret.Contains(c.ToString()))
                    {
                        System.Console.WriteLine("The character \"" + c.ToString() + "\" is not permitted.");
                        itisdone = false;
                    }
                }
            }

            m_isPrompting = false;
            return ret;
        }

        public string CmdPrompt(string p, string def, List<char> excludedCharacters)
        {
            m_isPrompting = true;
            bool itisdone = false;
            string ret = String.Empty;
            while (!itisdone)
            {
                itisdone = true;
                ret = CmdPrompt(p, def);

                if (ret == String.Empty)
                {
                    ret = def;
                }
                else
                {
                    foreach (char c in excludedCharacters)
                    {
                        if (ret.Contains(c.ToString()))
                        {
                            System.Console.WriteLine("The character \"" + c.ToString() + "\" is not permitted.");
                            itisdone = false;
                        }
                    }
                }
            }
            m_isPrompting = false;

            return ret;
        }

        // Displays a command prompt and returns a default value, user may only enter 1 of 2 options
        public string CmdPrompt(string prompt, string defaultresponse, List<string> options)
        {
            m_isPrompting = true;
            m_promptOptions = new List<string>(options);

            bool itisdone = false;
            string optstr = String.Empty;
            foreach (string s in options)
                optstr += " " + s;

            string temp = CmdPrompt(prompt, defaultresponse);
            while (itisdone == false)
            {
                if (options.Contains(temp))
                {
                    itisdone = true;
                }
                else
                {
                    System.Console.WriteLine("Valid options are" + optstr);
                    temp = CmdPrompt(prompt, defaultresponse);
                }
            }
            m_isPrompting = false;
            m_promptOptions.Clear ();
            return temp;
        }

        // Displays a prompt and waits for the user to enter a string, then returns that string
        // (Done with no echo and suitable for passwords)
        public string PasswdPrompt(string p)
        {
            m_isPrompting = true;
            string line = ReadLine(p + ": ", false, false);
            m_isPrompting = false;
            return line;
        }

        public virtual void Output(string text, Level level)
        {
            MainConsole.TriggerLog(level, text);
            System.Console.WriteLine(text);
        }

        public virtual void Output(string text)
        {
            Output(text, Level.Debug);
        }

        public virtual void LockOutput()
        {
        }

        public virtual void UnlockOutput()
        {
        }

        /// <summary>
        /// The default prompt text.
        /// </summary>
        public virtual string DefaultPrompt
        {
            set { m_defaultPrompt = value; }
            get { return m_defaultPrompt; }
        }
        protected string m_defaultPrompt;

        public virtual string Name
        {
            get { return "CommandConsole"; }
        }

        public Commands m_Commands = new Commands ();

        public Commands Commands
        {
            get
            {
                return m_Commands;
            }
            set
            {
                m_Commands = value;
            }
        }

        public IScene ConsoleScene
        {
            get
            {
                return m_ConsoleScene;
            }
            set
            {
                m_ConsoleScene = value;
            }
        }
        public IScene m_ConsoleScene = null;
        
        public void Dispose()
        {
        }


        public void EndConsoleProcessing()
        {
            Processing = false;
        }

        public bool Processing = true;
#if !NET_4_0
        private delegate void PromptEvent();
        private IAsyncResult result = null;
        private PromptEvent action = null;
        private Object m_consoleLock = new Object();
        private bool m_calledEndInvoke = false;
#endif

        /// <summary>
        /// Starts the prompt for the console. This will never stop until the region is closed.
        /// </summary>
        public void ReadConsole ()
        {
            while (true)
            {
#if !NET_4_0
                if (!Processing)
                {
                    throw new Exception ("Restart");
                }
                lock (m_consoleLock)
                {
                    if (action == null)
                    {
                        action = Prompt;
                        result = action.BeginInvoke (null, null);
                        m_calledEndInvoke = false;
                    }
                    try
                    {
                        if ((!result.IsCompleted) &&
                            (!result.AsyncWaitHandle.WaitOne (5000, false) || !result.IsCompleted))
                        {

                        }
                        else if (action != null &&
                            !result.CompletedSynchronously &&
                            !m_calledEndInvoke)
                        {
                            m_calledEndInvoke = true;
                            action.EndInvoke (result);
                            action = null;
                            result = null;
                        }
                    }
                    catch(Exception ex)
                    {
                        //Eat the exception and go on
                        Output ("[Console]: Failed to execute command: " + ex.ToString ());
                        action = null;
                        result = null;
                    }
                }
#else
                Task prompt = TaskEx.Run(() => { Prompt(); });
                if (!Processing)
                    throw new Exception("Restart");
                while (!Task.WaitAll(new Task[1] { prompt }, 1000))
                {
                    if (!Processing)
                        throw new Exception("Restart");
                }
#endif
            }
        }
    }
}
