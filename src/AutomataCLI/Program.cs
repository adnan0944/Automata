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

            //new RegexMetrics(@"(a|b|c|d)*[abcd]*|(gh+)+");
            //new RegexMetrics(@"abc");
            //new RegexMetrics(@"^abc");
            //new RegexMetrics(@"abc$");
            //new RegexMetrics(@"^abc$");
            //new RegexMetrics(@"^abc$|def");

            // Check args
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: query-file.json\n  query-file.json should be NDJSON-formatted\n  Each object should contain at least the key 'pattern'");
                System.Environment.Exit(1);
            }
            string queryFile = args[0];

            // Read file
            System.IO.StreamReader file = new System.IO.StreamReader(queryFile);
            string line;
            while ((line = file.ReadLine()) != null)
            {
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
            automataMeasures = MeasureAutomaton(pattern);
            efreeNFAGraph = EFreeNFAGraph(pattern);

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
            autMeasures["valid"] = 0;

            try
            {
                Console.Error.WriteLine("Automaton measurements");

                CharSetSolver solver = new CharSetSolver();
                RegexToAutomatonConverter<BDD> conv = new RegexToAutomatonConverter<BDD>(solver);

                // NFA measurements
                bool hasLoops = false;
                Automaton<BDD> aut = conv.Convert(pattern, RegexOptions.None, false, false, out hasLoops);
                autMeasures["nfa_orig_nStates"] = Automaton_nStates(aut);
                autMeasures["nfa_orig_nEdges"] = Automaton_nEdges(aut);
                autMeasures["nfa_orig_nEdgePairs"] = Automaton_nEdgePairs(aut);
                autMeasures["nfa_orig_isLoopFree"] = Automaton_isLoopFree(aut);
                autMeasures["finiteLanguage"] = hasLoops ? 0 : 1;
                //aut.ShowGraph("nfa_orig_pattern-" + pattern.GetHashCode());

                // e-free NFA measurements
                Automaton<BDD> efree = aut.RemoveEpsilons();
                autMeasures["nfa_efree_nStates"] = Automaton_nStates(efree);
                autMeasures["nfa_efree_nEdges"] = Automaton_nEdges(efree);
                autMeasures["nfa_efree_nEdgePairs"] = Automaton_nEdgePairs(efree);
                autMeasures["nfa_efree_isLoopFree"] = Automaton_isLoopFree(efree);

                // Minimal NFA measurements
                Automaton<BDD> minaut = aut.Minimize();
                autMeasures["nfa_min_nStates"] = Automaton_nStates(minaut);
                autMeasures["nfa_min_nEdges"] = Automaton_nEdges(minaut);
                autMeasures["nfa_min_nEdgePairs"] = Automaton_nEdgePairs(minaut);
                autMeasures["nfa_min_isLoopFree"] = Automaton_isLoopFree(minaut);

                // DFA measurements
                Automaton<BDD> detaut = aut.Determinize();
                autMeasures["dfa_nStates"] = Automaton_nStates(detaut);
                autMeasures["dfa_nEdges"] = Automaton_nEdges(detaut);
                autMeasures["dfa_nEdgePairs"] = Automaton_nEdgePairs(detaut);
                autMeasures["dfa_isLoopFree"] = Automaton_isLoopFree(detaut);

                Tuple<BDD[], int> shortestPath = detaut.FindShortestFinalPath(detaut.InitialState);
                autMeasures["dfa_shortestMatchingInput"] = shortestPath.Item1.Length;

                // It worked!
                autMeasures["valid"] = 1;
            }
            catch (AutomataException e)
            {
                Console.Error.WriteLine("Error making automaton from /{0}/: {1}", pattern, e);
            }

            return autMeasures;
        }

        static string EFreeNFAGraph(string pattern)
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
                    .RemoveEpsilons()
                    //.Normalize() // Unnecessary
                    //.Minimize() // This has surprising effects, e.g. lopping off half of the graph if there is an OR. Not sure if this is an Automata bug.
                    ;
                Console.Error.WriteLine("Built aut");

                graph = Automaton_graph(aut);
                //aut.ShowGraph("aut-" + pattern.GetHashCode());
            }
            catch (AutomataException e)
            {
                Console.Error.WriteLine("Error making automaton from /{0}/: {1}", pattern, e);
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
