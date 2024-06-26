﻿using System.Collections.Generic;

namespace LlamaIndex.Core.Schema;

public class RelatedNodeInfo(string nodeId, NodeType nodeType, Dictionary<string, object>? metadata = null)
{
    public string NodeId { get; } = nodeId;
    public NodeType NodeType { get; } = nodeType;
    public Dictionary<string, object>? Metadata { get; } = metadata;
}