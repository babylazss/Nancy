namespace Nancy.Routing.Trie.Nodes
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A base class representing a node in the route trie
    /// </summary>
    public abstract class TrieNode
    {
        private readonly TrieNodeFactory nodeFactory;

        /// <summary>
        /// Gets or sets the parent node
        /// </summary>
        public TrieNode Parent { get; protected set; }

        /// <summary>
        /// Gets or sets the segment from the route definition that this node represents
        /// </summary>
        public string RouteDefinitionSegment { get; protected set; }

        /// <summary>
        /// Gets or sets the children of this node
        /// </summary>
        public IDictionary<string, TrieNode> Children { get; protected set; }

        /// <summary>
        /// Gets or sets the node data stored at this node, which will be converted
        /// into the <see cref="MatchResult"/> if a match is found
        /// </summary>
        public IList<NodeData> NodeData { get; protected set; }

        /// <summary>
        /// Additional parameters to set that can be determined at trie build time
        /// </summary>
        public IDictionary<string, object> AdditionalParameters { get; protected set; }

        /// <summary>
        /// Score for this node
        /// </summary>
        public abstract int Score { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrieNode"/> class
        /// </summary>
        /// <param name="parent">Parent node</param>
        /// <param name="segment">Segment of the route definition</param>
        /// <param name="nodeFactory">Factory for creating new nodes</param>
        protected TrieNode(TrieNode parent, string segment, TrieNodeFactory nodeFactory)
        {
            this.nodeFactory = nodeFactory;
            this.Parent = parent;
            this.RouteDefinitionSegment = segment;

            this.Children = new Dictionary<string, TrieNode>();
            this.AdditionalParameters = new Dictionary<string, object>();
            this.NodeData = new List<NodeData>();
        }

        /// <summary>
        /// Add a new route to the trie
        /// </summary>
        /// <param name="segments">The segments of the route definition</param>
        /// <param name="moduleKey">The module key the route comes from</param>
        /// <param name="routeIndex">The route index in the module</param>
        /// <param name="routeDescription">The route description</param>
        public void Add(string[] segments, string moduleKey, int routeIndex, RouteDescription routeDescription)
        {
            this.Add(segments, -1, 0, 0, moduleKey, routeIndex, routeDescription);    
        }

        /// <summary>
        /// Add a new route to the trie
        /// </summary>
        /// <param name="segments">The segments of the route definition</param>
        /// <param name="currentIndex">Current index in the segments array</param>
        /// <param name="currentScore">Current score for this route</param>
        /// <param name="nodeCount">Number of nodes added for this route</param>
        /// <param name="moduleKey">The module key the route comes from</param>
        /// <param name="routeIndex">The route index in the module</param>
        /// <param name="routeDescription">The route description</param>
        public virtual void Add(string[] segments, int currentIndex, int currentScore, int nodeCount, string moduleKey, int routeIndex, RouteDescription routeDescription)
        {
            if (this.NoMoreSegments(segments, currentIndex))
            {
                this.NodeData.Add(this.BuildNodeData(nodeCount, currentScore + this.Score, moduleKey, routeIndex, routeDescription));
                return;
            }

            nodeCount++;
            currentIndex++;
            TrieNode child;

            if (!this.Children.TryGetValue(segments[currentIndex], out child))
            {
                child = this.nodeFactory.GetNodeForSegment(this, segments[currentIndex]);
                this.Children.Add(segments[currentIndex], child);
            }

            child.Add(segments, currentIndex, currentScore + this.Score, nodeCount, moduleKey, routeIndex, routeDescription);
        }

        /// <summary>
        /// Gets all matches for a given requested route
        /// </summary>
        /// <param name="segments">Requested route segments</param>
        /// <param name="context">Current Nancy context</param>
        /// <returns>A collection of <see cref="MatchResult"/> objects</returns>
        public virtual IEnumerable<MatchResult> GetMatches(string[] segments, NancyContext context)
        {
            return this.GetMatches(segments, 0, new Dictionary<string, object>(), context);
        }

        /// <summary>
        /// Gets all matches for a given requested route
        /// </summary>
        /// <param name="segments">Requested route segments</param>
        /// <param name="currentIndex">Current index in the route segments</param>
        /// <param name="capturedParameters">Currently captured parameters</param>
        /// <param name="context">Current Nancy context</param>
        /// <returns>A collection of <see cref="MatchResult"/> objects</returns>
        public virtual IEnumerable<MatchResult> GetMatches(string[] segments, int currentIndex, IDictionary<string, object> capturedParameters, NancyContext context)
        {
            this.AddAdditionalParameters(capturedParameters);

            var segmentMatch = this.Match(segments[currentIndex]);
            if (segmentMatch == SegmentMatch.NoMatch)
            {
                return MatchResult.NoMatches;
            }

            foreach (var capturedParameter in segmentMatch.CapturedParameters)
            {
                capturedParameters[capturedParameter.Key] = capturedParameter.Value;
            }

            if (this.NoMoreSegments(segments, currentIndex))
            {
                return this.BuildResults(capturedParameters) ?? MatchResult.NoMatches;
            }

            currentIndex++;

            return this.GetMatchingChildren(segments, currentIndex, capturedParameters, context);
        }

        /// <summary>
        /// Gets a string representation of all routes
        /// </summary>
        /// <returns>Collection of strings, each representing a route</returns>
        public virtual IEnumerable<string> GetRoutes()
        {
            var routeList = new List<string>(this.Children.Values.SelectMany(c => c.GetRoutes())
                             .Select(s => (this.RouteDefinitionSegment ?? string.Empty) + "/" + s));

            if (this.NodeData.Any())
            {
                var node = this.NodeData.First();
                var resultData = string.Format("{0} (Segments: {1} Score: {2})", this.RouteDefinitionSegment ?? "/", node.RouteLength, node.Score);
                routeList.Add(resultData);
            }

            return routeList;
        }

        /// <summary>
        /// Build the node data that will be used to create the <see cref="MatchResult"/>
        /// We calculate/store as much as possible at build time to reduce match time.
        /// </summary>
        /// <param name="nodeCount">Number of nodes in the route</param>
        /// <param name="score">Score for the route</param>
        /// <param name="moduleKey">The module key the route comes from</param>
        /// <param name="routeIndex">The route index in the module</param>
        /// <param name="routeDescription">The route description</param>
        /// <returns>A NodeData instance</returns>
        protected virtual NodeData BuildNodeData(int nodeCount, int score, string moduleKey, int routeIndex, RouteDescription routeDescription)
        {
            return new NodeData
                       {
                           Method = routeDescription.Method,
                           RouteIndex = routeIndex,
                           RouteLength = nodeCount,
                           Score = score,
                           Condition = routeDescription.Condition,
                           ModuleKey = moduleKey,
                       };
        }

        /// <summary>
        /// Returns whether we are at the end of the segments
        /// </summary>
        /// <param name="segments">Route segments</param>
        /// <param name="currentIndex">Current index</param>
        /// <returns>True if no more segments left, false otherwise</returns>
        protected bool NoMoreSegments(string[] segments, int currentIndex)
        {
            return currentIndex >= segments.Length - 1;
        }

        /// <summary>
        /// Adds the additional parameters to the captured parameters
        /// </summary>
        /// <param name="capturedParameters">Currently captured parameters</param>
        protected void AddAdditionalParameters(IDictionary<string, object> capturedParameters)
        {
            foreach (var additionalParameter in this.AdditionalParameters)
            {
                capturedParameters[additionalParameter.Key] = additionalParameter.Value;
            }
        }

        /// <summary>
        /// Build the results collection from the captured parameters if
        /// this node is the end result
        /// </summary>
        /// <param name="capturedParameters">Currently captured parameters</param>
        /// <returns>Array of <see cref="MatchResult"/> objects corresponding to each set of <see cref="NodeData"/> stored at this node</returns>
        protected IEnumerable<MatchResult> BuildResults(IDictionary<string, object> capturedParameters)
        {
            if (!this.NodeData.Any())
            {
                return null;
            }

            return this.NodeData.Select(n => n.ToResult(capturedParameters));
        }

        /// <summary>
        /// Gets all the matches from this node's children
        /// </summary>
        /// <param name="segments">Requested route segments</param>
        /// <param name="currentIndex">Current index</param>
        /// <param name="capturedParameters">Currently captured parameters</param>
        /// <param name="context">Current Nancy context</param>
        /// <returns>Collection of <see cref="MatchResult"/> objects</returns>
        protected IEnumerable<MatchResult> GetMatchingChildren(string[] segments, int currentIndex, IDictionary<string, object> capturedParameters, NancyContext context)
        {
            return this.Children.Values.SelectMany(k => k.GetMatches(segments, currentIndex, new Dictionary<string, object>(capturedParameters), context));
        }

        /// <summary>
        /// Matches the segment for a requested route
        /// </summary>
        /// <param name="segment">Segment string</param>
        /// <returns>A <see cref="SegmentMatch"/> instance representing the result of the match</returns>
        public abstract SegmentMatch Match(string segment);
    }
}