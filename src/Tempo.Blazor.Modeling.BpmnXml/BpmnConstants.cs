namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>BPMN 2.0 namespace and notation constants used by the headless converter.</summary>
public static class BpmnConstants
{
    /// <summary>The BPMN 2.0 model namespace (<c>http://www.omg.org/spec/BPMN/20100524/MODEL</c>).</summary>
    public const string ModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>The BPMN 2.0 diagram-interchange namespace (<c>http://www.omg.org/spec/BPMN/20100524/DI</c>).</summary>
    public const string DiagramNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";

    /// <summary>The BPMN 2.0 DC namespace (<c>http://www.omg.org/spec/DD/20100524/DC</c>).</summary>
    public const string DCNamespace = "http://www.omg.org/spec/DD/20100524/DC";

    /// <summary>The BPMN 2.0 DI namespace (<c>http://www.omg.org/spec/DD/20100524/DI</c>).</summary>
    public const string DINamespace = "http://www.omg.org/spec/DD/20100524/DI";

    /// <summary>The modeling notation key for BPMN 2.0 used on imported models, elements and views.</summary>
    public const string Bpmn2Notation = "bpmn2";
}

/// <summary>
/// Semantic element type names the importer emits. Duplicated on purpose from the internal
/// <c>BpmnModelingCatalog</c> (Tempo.Blazor.Modeling) — the converter must not depend on the
/// internal type; the corpus parity test keeps the two lists from drifting.
/// </summary>
public static class BpmnSemanticTypes
{
    /// <summary>Generic BPMN task.</summary>
    public const string Task = "Task";

    /// <summary>BPMN user task.</summary>
    public const string UserTask = "UserTask";

    /// <summary>BPMN service task.</summary>
    public const string ServiceTask = "ServiceTask";

    /// <summary>BPMN send task.</summary>
    public const string SendTask = "SendTask";

    /// <summary>BPMN receive task.</summary>
    public const string ReceiveTask = "ReceiveTask";

    /// <summary>BPMN script task.</summary>
    public const string ScriptTask = "ScriptTask";

    /// <summary>BPMN business rule task.</summary>
    public const string BusinessRuleTask = "BusinessRuleTask";

    /// <summary>BPMN manual task.</summary>
    public const string ManualTask = "ManualTask";

    /// <summary>BPMN (ad-hoc) subprocess or transaction.</summary>
    public const string SubProcess = "SubProcess";

    /// <summary>BPMN event subprocess (triggeredByEvent).</summary>
    public const string EventSubProcess = "EventSubProcess";

    /// <summary>BPMN call activity.</summary>
    public const string CallActivity = "CallActivity";

    /// <summary>BPMN start event.</summary>
    public const string StartEvent = "StartEvent";

    /// <summary>BPMN end event.</summary>
    public const string EndEvent = "EndEvent";

    /// <summary>BPMN intermediate catch event.</summary>
    public const string IntermediateCatchEvent = "IntermediateCatchEvent";

    /// <summary>BPMN intermediate throw event.</summary>
    public const string IntermediateThrowEvent = "IntermediateThrowEvent";

    /// <summary>BPMN boundary event.</summary>
    public const string BoundaryEvent = "BoundaryEvent";

    /// <summary>BPMN exclusive gateway.</summary>
    public const string ExclusiveGateway = "ExclusiveGateway";

    /// <summary>BPMN parallel gateway.</summary>
    public const string ParallelGateway = "ParallelGateway";

    /// <summary>BPMN inclusive gateway.</summary>
    public const string InclusiveGateway = "InclusiveGateway";

    /// <summary>BPMN complex gateway.</summary>
    public const string ComplexGateway = "ComplexGateway";

    /// <summary>BPMN event-based gateway.</summary>
    public const string EventBasedGateway = "EventBasedGateway";

    /// <summary>BPMN pool (collaboration participant).</summary>
    public const string Pool = "Pool";

    /// <summary>BPMN lane.</summary>
    public const string Lane = "Lane";

    /// <summary>BPMN data object (reference).</summary>
    public const string DataObject = "DataObject";

    /// <summary>BPMN data store (reference).</summary>
    public const string DataStore = "DataStore";

    /// <summary>BPMN group.</summary>
    public const string Group = "Group";

    /// <summary>BPMN text annotation.</summary>
    public const string TextAnnotation = "TextAnnotation";
}

/// <summary>
/// Semantic relationship type names the importer emits. Duplicated on purpose from the internal
/// <c>BpmnModelingCatalog</c>; the corpus parity test keeps the two lists from drifting.
/// </summary>
public static class BpmnRelationshipTypes
{
    /// <summary>BPMN sequence flow.</summary>
    public const string SequenceFlow = "SequenceFlow";

    /// <summary>BPMN conditional sequence flow (carries a conditionExpression).</summary>
    public const string ConditionalFlow = "ConditionalFlow";

    /// <summary>BPMN default sequence flow (a gateway's default outgoing flow).</summary>
    public const string DefaultFlow = "DefaultFlow";

    /// <summary>BPMN message flow (collaboration).</summary>
    public const string MessageFlow = "MessageFlow";

    /// <summary>BPMN association.</summary>
    public const string Association = "Association";

    /// <summary>BPMN data input association.</summary>
    public const string DataInputAssociation = "DataInputAssociation";

    /// <summary>BPMN data output association.</summary>
    public const string DataOutputAssociation = "DataOutputAssociation";
}
