using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.RegularExpressions;
using Microsoft.Automata;

using Newtonsoft.Json;

namespace AutomataCLI
{
    class Program
    {
        public static int timeout_ms = 5 * 1000;
        static void Main(string[] args)
        {
            String pattern = @"^abc(e*|f)+$";
            pattern = @"a*b+c?d+?(e|f)(?<h>g)\1\k<h>";
            pattern = @"^a+(b)c*[e].[a-z][^f]\sa|b\d\wz?z+?z*?\S\Z\b\W\1\D\B\z(?:ncg)$";
            pattern = @"(0|1)*1(0|1)(0|1)(0|1)(0|1)(0|1)(0|1)(0|1)"; // Exponential state blow-up
            pattern = @"(?=a)"; // LKA
            pattern = @"(?!a)"; // NLKA
            pattern = @"(?<=a)"; // LKB
            pattern = @"(?<!a)"; // NLKB
            pattern = @"(a)\1"; // CG BKR
            pattern = @"(?<name>a)\1"; // PNG BKR
            pattern = @"(?<name>a)\k<name>"; // PNG BKRN
            pattern = @"[abc]"; // CCC
            pattern = @"[^a]"; // NCCC
            pattern = @"[a-z]"; // RNG
            pattern = @"[^a-z]"; // NCCC RNG
            pattern = @"a{5}"; // SNG
            pattern = @"a{5,}"; // LWB
            pattern = @"a{5,6}"; // DBB
            pattern = @"a(?i)b"; // OPT
            pattern = @"a(?i-x:(\w))b"; // OPT CG

            //new RegexMetrics(@"ab{48}c");
            //new RegexMetrics(@"style-src 'nonce-.{48}'");
            //new RegexMetrics(@"/((?:^|[&(])[ \t]*)if(?: ?\/[a-z?](?:[ :](?:'[^']*' |\S +)) ?)*(?: not) ? (?: cmdextversion \d +| defined \w +| errorlevel \d +| exist \S +| (?: '[^']*'|\S+)?(?:==| (?:equ|neq|lss|leq|gtr|geq) )(?:'[^']*' |\S +)))");
            //new RegexMetrics(@"(a|b|c|d)*[abcd]*|(gh+)+");
            //new RegexMetrics(@"[&?]file=([^&]+)");
            //while (true) { }
            //new RegexMetrics(@"^abc");
            //new RegexMetrics(@"abc$");
            //new RegexMetrics(@"^abc$");
            //new RegexMetrics(@"^abc$|def");

            //Console.WriteLine("Here");
            //pattern = @"(?<name>abc)(?:b)(c)(d(e))"; // PNG BKR
            //pattern = @"a(?i-x:(\w))b"; // OPT CG
            pattern = @"(c[1234567890])\(d(e)\)\1\2\1\1\1\1"; // CG BKR
            new RegexMetrics(pattern);
            Console.ReadKey();
            // Check args
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: query-file.json\n  query-file.json should be NDJSON-formatted\n  Each object should contain at least the key 'pattern'\n  Warning: May encounter OOM errors on unusual regexes.");
                System.Environment.Exit(1);
            }
            string queryFile = args[0];

            // Read file
            System.IO.StreamReader file = new System.IO.StreamReader(queryFile);
            string line;
            int i = 0;
            while ((line = file.ReadLine()) != null)
            {
                Console.Error.WriteLine("Line {0}: {1}", i, line);
                i++;

                ProcessLine(line);
            }
        }

        /* line -> JSON -> Analyze -> JSON -> stdout */
        static void ProcessLine(string line)
        {
            // Load as JSON
            dynamic queryObj = JsonConvert.DeserializeObject(line);
            string pattern = queryObj.pattern;

            // Create RMQ and analyze
            RegexMetricsQuery rmq = new RegexMetricsQuery(pattern);
            rmq.Analyze();

            // Emit to stdout
            Console.WriteLine(JsonConvert.SerializeObject(rmq));
        }

    }

    class RegexMetricsQuery
    {
        public string pattern;
        public RegexMetrics regexMetrics;

        public RegexMetricsQuery(string pattern)
        {
            this.pattern = pattern;
            regexMetrics = null;
        }

        public void Analyze()
        {
            regexMetrics = new RegexMetrics(pattern);
        }
    }

    /* Compute regex metrics. */
    class RegexMetrics
    {
        public bool validCSharpRegex { get; set; }
        public int regexLen { get; set; }
        public Dictionary<string, int> featureVector { get; set; }
        public Dictionary<int, Dictionary<string, string>> groupVector { get; set; }
        public Dictionary<string, int> automataMeasures { get; set; }
        public string efreeNFAGraph { get; set; }

        public RegexMetrics(string pattern)
        {
            // Is it a valid regex?
            validCSharpRegex = IsValidCSharpRegex(pattern);
            if (!validCSharpRegex)
                return;

            regexLen = pattern.Length;
            featureVector = MeasureFeatureVector(pattern);
            groupVector = MeasureGroups(pattern);
            automataMeasures = MeasureAutomaton(pattern);
            efreeNFAGraph = EFreeLooplessNFAGraph(pattern);

            return;
        }

        bool IsValidCSharpRegex(string pattern)
        {
            Console.Error.WriteLine("Checking whether pattern is valid: /{0}/", pattern);
            try
            {
                Regex rx = new Regex(pattern);
                Console.Error.WriteLine("Valid regex: /{0}/", pattern);
                validCSharpRegex = true;
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine("Invalid regex: /{0}/", pattern);
                validCSharpRegex = false;
            }

            return validCSharpRegex;
        }

        public Dictionary<int, Dictionary<string, string>> MeasureGroups(string pattern)
        {
            Dictionary<int, Dictionary<string, string>> cg = new Dictionary<int, Dictionary<string, string>>();

            try
            {
                Console.Error.WriteLine("Capture group extraction");

                CharSetSolver solver = new CharSetSolver();
                RegexToAutomatonConverter<BDD> conv = new RegexToAutomatonConverter<BDD>(solver);

                cg = conv.GroupVector(pattern);
                DumpGroupVector(cg);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error getting feature vector from /{0}/: {1}", pattern, e);
            }

            return cg;
        }
        public Dictionary<string, int> MeasureFeatureVector(string pattern)
        {
            Dictionary<string, int> fv = new Dictionary<string, int>();
            fv["valid"] = 0;

            try
            {
                Console.Error.WriteLine("Feature extraction");

                CharSetSolver solver = new CharSetSolver();
                RegexToAutomatonConverter<BDD> conv = new RegexToAutomatonConverter<BDD>(solver);

                fv = conv.FeatureVector(pattern);
                DumpFeatureVector(fv);
                fv["valid"] = 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error getting feature vector from /{0}/: {1}", pattern, e);
            }

            return fv;
        }

        public Dictionary<string, int> MeasureAutomaton(string pattern)
        {
            Dictionary<string, int> autMeasures = new Dictionary<string, int>();

            try
            {
                Console.Error.WriteLine("Automaton measurements");
                // We know nothing. Could time out at several points.
                autMeasures["nfa_orig_completeInfo"] = 0;
                autMeasures["nfa_efree_completeInfo"] = 0;
                autMeasures["dfa_completeInfo"] = 0;

                CharSetSolver solver = new CharSetSolver();
                RegexToAutomatonConverter<BDD> conv = new RegexToAutomatonConverter<BDD>(solver);

                #region NFA measurements
                Console.Error.WriteLine("NFA");

                bool hasLoops = false;
                Automaton<BDD> aut = conv.Convert(pattern, RegexOptions.None, false, false, out hasLoops);
                Console.Error.WriteLine("  nfa has {0} states, {1} edges", Automaton_nStates(aut), Automaton_nEdges(aut));
                autMeasures["nfa_orig_nStates"] = Automaton_nStates(aut);
                autMeasures["nfa_orig_nEdges"] = Automaton_nEdges(aut);
                autMeasures["nfa_orig_nEdgePairs"] = Automaton_nEdgePairs(aut);
                //autMeasures["nfa_orig_isLoopFree"] = Automaton_isLoopFree(aut);
                autMeasures["nfa_orig_finiteLanguage"] = hasLoops ? 0 : 1;
                //aut.ShowGraph("nfa_orig_pattern-" + pattern.GetHashCode());

                autMeasures["nfa_orig_completeInfo"] = 1;
                #endregion

                #region e-free NFA measurements
                Console.Error.WriteLine("e-free NFA");
                try
                {
                    Automaton<BDD> efree = aut.RemoveEpsilons(Program.timeout_ms);
                    Console.Error.WriteLine("  efree has {0} states, {1} edges", Automaton_nStates(efree), Automaton_nEdges(efree));
                    autMeasures["nfa_efree_nStates"] = Automaton_nStates(efree);
                    autMeasures["nfa_efree_nEdges"] = Automaton_nEdges(efree);
                    autMeasures["nfa_efree_nEdgePairs"] = Automaton_nEdgePairs(efree);
                    //autMeasures["nfa_efree_isLoopFree"] = Automaton_isLoopFree(efree);

                    autMeasures["nfa_efree_completeInfo"] = 1;
                }
                catch (TimeoutException e)
                {
                    autMeasures["nfa_efree_timeout"] = 1;
                    Console.Error.WriteLine("Timeout generating e-free nfa from /{0}/", pattern);
                }

                #endregion

                // Minimal NFA measurements
                // These APIs seem untrustworthy. Odd behavior, require DFA, etc.
                //Console.Error.WriteLine("Moore-minimized NFA");
                //try
                //{
                //    Automaton<BDD> minaut = aut.MinimizeMoore(Program.timeout_ms);
                //    autMeasures["nfa_min_nStates"] = Automaton_nStates(minaut);
                //    autMeasures["nfa_min_nEdges"] = Automaton_nEdges(minaut);
                //    autMeasures["nfa_min_nEdgePairs"] = Automaton_nEdgePairs(minaut);
                //    //autMeasures["nfa_min_isLoopFree"] = Automaton_isLoopFree(minaut);
                //
                //    autMeasures["nfa_min_timeout"] = 0;
                //} catch (TimeoutException e)
                //{
                //    autMeasures["nfa_min_timeout"] = 1;
                //}

                #region DFA measurements
                Console.Error.WriteLine("DFA");
                try
                {
                    Console.Error.WriteLine("  Determinizing (timeout {0} ms)", Program.timeout_ms);
                    Automaton<BDD> dfa = aut.Determinize(Program.timeout_ms);
                    Console.Error.WriteLine("  dfa has {0} states, {1} edges", Automaton_nStates(dfa), Automaton_nEdges(dfa));

                    autMeasures["dfa_nStates"] = Automaton_nStates(dfa);
                    autMeasures["dfa_nEdges"] = Automaton_nEdges(dfa);
                    autMeasures["dfa_nEdgePairs"] = Automaton_nEdgePairs(dfa);

                    Tuple<BDD[], int> dfaShortestPath = dfa.FindShortestFinalPath(dfa.InitialState);
                    autMeasures["dfa_shortestMatchingInput"] = dfaShortestPath.Item1.Length;

                    autMeasures["dfa_timeout"] = 0;
                    autMeasures["dfa_completeInfo"] = 1;

                    if (false)
                    {
                        /* Minimizing results in OOM errors and subsequent program instability.
                         * Regexes with many states -- e.g. /abc{50}/ -- have problems.
                         * This is not unheard-of in real regexes, so minimizing is tricky.
                         * I found at least one place where the timeout was not being propagated, but I don't see it as worthwhile to keep measuring.
                         * Knowing how often there is NFA -> DFA blow-up is interesting.
                         * Knowing the size of the minimized DFA -- since it is expensive to do so for an exploded DFA -- is not worthwhile.
                         * Seems like that would be an unusual regex engine optimization.
                         * Anyway, the upshot is, let's not measure this. */
                        Console.Error.WriteLine("  Minimizing");
                        Automaton<BDD> dfamin = dfa.MinimizeMoore(Program.timeout_ms);
                        Console.Error.WriteLine("  dfamin has {0} states, {1} edges", Automaton_nStates(dfamin), Automaton_nEdges(dfamin));

                        autMeasures["dfamin_nStates"] = Automaton_nStates(dfamin);
                        autMeasures["dfamin_nEdges"] = Automaton_nEdges(dfamin);
                        autMeasures["dfamin_nEdgePairs"] = Automaton_nEdgePairs(dfamin);

                        Tuple<BDD[], int> shortestPath = dfamin.FindShortestFinalPath(dfamin.InitialState);
                        autMeasures["dfamin_shortestMatchingInput"] = shortestPath.Item1.Length;

                        autMeasures["dfamin_timeout"] = 0;
                    }
                    #endregion
                }
                catch (TimeoutException e)
                {
                    autMeasures["dfa_timeout"] = 1;
                    Console.Error.WriteLine("Timeout getting dfa information from /{0}/", pattern);
                }
            }
            catch (System.OutOfMemoryException e)
            {
                Console.Error.WriteLine("MeasureAutomaton: Error, System.OutOfMemoryException on /{0}/", pattern);
                System.Environment.Exit(1);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error making automaton from /{0}/: {1}", pattern, e);
            }

            return autMeasures;
        }

        /* Create e-free NFA graph.
         * Graph has no loops, facilitating the computation of the basis inputs.
         * Also reduces {x,y} to {1} to avoid combinatorial basis path explosion.
         */
        static string EFreeLooplessNFAGraph(string pattern)
        {
            string graph = "";
            try
            {
                Console.Error.WriteLine("Graph generation");

                CharSetSolver solver = new CharSetSolver();
                RegexToAutomatonConverter<BDD> conv = new RegexToAutomatonConverter<BDD>(solver);

                // NFA measurements
                bool removeLoops = true;
                bool hasLoops = false;
                Automaton<BDD> aut = conv.Convert(pattern, RegexOptions.None, false, removeLoops, out hasLoops)
                    .RemoveEpsilons(Program.timeout_ms)
                    //.Normalize() // Unnecessary
                    //.Minimize() // This has surprising effects, e.g. lopping off half of the graph if there is an OR. Not sure if this is an Automata bug.
                    ;
                Console.Error.WriteLine("Built aut");

                graph = Automaton_graph(aut);
                //aut.ShowGraph("aut-" + pattern.GetHashCode());
            }
            catch (System.OutOfMemoryException e)
            {
                Console.Error.WriteLine("EFreeLooplessNFAGraph: Error, System.OutOfMemoryException on /{0}/", pattern);
                System.Environment.Exit(1);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error making automaton from /{0}/: {1}", pattern, e);
                graph = "TIMEOUT";
            }

            return graph;
        }

        static int Automaton_nStates(Automaton<BDD> aut)
        {
            return CountEnumerable(aut.GetStates());
        }

        static int Automaton_nEdges(Automaton<BDD> aut)
        {
            return CountEnumerable(aut.GetMoves());
        }

        static int Automaton_nEdgePairs(Automaton<BDD> aut)
        {
            HashSet<Tuple<int, int>> edgePairs = new HashSet<Tuple<int, int>>();
            foreach (Move<BDD> move in aut.GetMoves())
            {
                edgePairs.Add(new Tuple<int, int>(move.SourceState, move.TargetState));
            }

            return edgePairs.Count();
        }

        /* This is not the most useful metric.
         * Regexes without ^ have a leading loop.
         * Regexes with or without a $ have a trailing loop, either
         *  to reject state ($) or accept state (no $).
         * Use the "hasLoops" measure computed during ScanRegex instead.
         */
        static int Automaton_isLoopFree(Automaton<BDD> aut)
        {
            return aut.IsLoopFree ? 1 : 0;
        }

        /* Return graph representation of the form:
         * SOURCE VERTEX(s)
         * ACCEPT VERTEX(s)
         * U V L // there is an edge U -> V labeled L
         * U V L // there is an edge U -> V labeled L
         */
        static string Automaton_graph(Automaton<BDD> aut)
        {
            // Pull out initial state and final state
            Console.Error.WriteLine("Initial, Final states");
            List<int> initialStates = new List<int>();
            initialStates.Add(aut.InitialState);

            List<int> finalStates = new List<int>();
            foreach (int finalState in aut.GetFinalStates())
            {
                finalStates.Add(finalState);
            }
            
            // Build string representation
            string graph = "";
            graph += String.Join(" ", initialStates);
            graph += "\n";
            graph += String.Join(" ", finalStates);
            Console.Error.WriteLine("Moves");
            foreach (Move<BDD> move in aut.GetMoves())
            {
                graph += "\n";
                graph += String.Join(" ", move.SourceState, move.TargetState, move.Label == null ? "" : move.Label.ToString());
            }       

            return graph;
        }

        static void DumpGroupVector(Dictionary<int, Dictionary<string, string>> cg)
        {
            foreach (int feature in cg.Keys)
            {
                Console.Error.WriteLine("{0} : {{'subexpression' : {1}, 'type' : {2}, 'pattern' : {3}, 'nBackreferenced' : {4}}}", feature, cg[feature]["Subexpression"], cg[feature]["type"], cg[feature]["pattern"], cg[feature]["nBackreferenced"]);
            }
        }

        static void DumpFeatureVector(Dictionary<string, int> fv)
        {
            foreach (string feature in fv.Keys)
            {
                Console.Error.WriteLine("{0} : {1}", feature, fv[feature]);
            }
        }

        void DumpTree(RegexNode root)
        {
            if (root == null)
            {
                return;
            }

            Console.Error.WriteLine("root (type {0}) has {1} children", root.Type(), root.ChildCount());
            Console.Error.WriteLine(root.ToString());
            /*
            for (int i = 0; i < root.ChildCount(); i++)
            {
                RegexNode child = root.Child(i);
                DumpTree(child);
            }
            */
            DumpTree(root.Next());
        }

        void SummarizeAutomaton(Automaton<BDD> aut)
        {
            Console.Error.WriteLine("Automaton:\n  {0} states\n  Deterministic? {1}",
                CountEnumerable(aut.GetStates()), aut.IsDeterministic);
        }

        static int CountEnumerable(dynamic enumerable)
        {
            int count = 0;
            foreach (var e in enumerable)
            {
                count++;
            }
            return count;
        }
    }
}
