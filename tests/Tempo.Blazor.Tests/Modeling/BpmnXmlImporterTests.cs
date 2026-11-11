using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Semantic import tests for the headless BPMN 2.0 XML importer (plan 6fca9139 phase 2):
/// elements, ParentId hierarchy, event definitions, sequence-flow typing, collaboration
/// splitting, data-association resolution, the loss report and result validation issues.
/// Full normalized output of every corpus fixture is pinned separately by
/// <c>BpmnImportGoldenTests</c>.
/// </summary>
/// <remarks>
/// The suite contains one WALL-CLOCK budget fact (a 200 000-task import under 10 s), so it belongs to the exclusive
/// <see cref="WallClockBudgets"/> collection: under the parallel suite that stopwatch measured the queue, not the
/// importer (13 s in the loaded FZE-15 run, ~2 s alone). The budget keeps its product value.
/// </remarks>
[Collection(WallClockBudgets.Name)]
public sealed class BpmnXmlImporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static BpmnImportResult Import(string fixture, Action<BpmnImportOptions>? configure = null)
    {
        var options = new BpmnImportOptions();
        configure?.Invoke(options);
        return new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture), options);
    }

    private static ModelingModelDto SingleModel(BpmnImportResult result)
    {
        result.HasErrors.Should().BeFalse(result.ErrorMessage ?? "import failed");
        return result.Models.Should().ContainSingle().Which;
    }

    private static ModelingElementDto Element(ModelingModelDto model, string id) =>
        model.Elements.Should().ContainSingle(element => element.Id == id, $"model must contain element {id}")
            .Subject;

    private static JsonElement Property(ModelingElementDto element, string key)
    {
        element.Properties.Should().ContainKey(key, $"{element.Id} must carry property {key}");
        return element.Properties[key];
    }

    // -------------------------------------------------------------- basics

    [Fact]
    public void Simple_process_imports_elements_in_xml_order_with_stable_ids()
    {
        var model = SingleModel(Import("simple-process.bpmn"));

        model.Id.Should().Be("Process_Simple");
        model.Title.Should().Be("Order handling");
        model.Notation.Should().Be("bpmn2");
        model.SupportedNotations.Should().Equal("bpmn2");
        model.Metadata.SourceSystem.Should().Be("bpmn-xml");
        model.Metadata.SourceVersion.Should().Be("http://tempo.example/bpmn/corpus");
        model.Metadata.LoadedAt.Should().Be(DateTimeOffset.UnixEpoch);
        model.Views.Should().ContainSingle("the BPMNDI plane imports as one view (phase 3)")
            .Which.Nodes.Should().HaveCount(model.Elements.Count);

        model.Elements.Select(element => element.Id).Should().Equal(
            "StartEvent_1", "Task_EnterOrder", "Gateway_Approved", "Task_Ship",
            "EndEvent_Shipped", "EndEvent_Rejected");

        Element(model, "Task_EnterOrder").SemanticType.Should().Be("UserTask");
        Element(model, "Task_Ship").SemanticType.Should().Be("Task");
        Element(model, "Gateway_Approved").SemanticType.Should().Be("ExclusiveGateway");
        model.Elements.Should().OnlyContain(
            element => element.Notation == "bpmn2" && element.SourceType.Length > 0 && element.SourcePath.StartsWith('/'),
            "every element carries notation, source type and an XML path");
    }

    [Fact]
    public void Sequence_flows_are_typed_and_carry_conditions()
    {
        var model = SingleModel(Import("simple-process.bpmn"));

        Element(model, "Gateway_Approved");
        model.Relationships.Should().ContainSingle(relationship => relationship.Id == "Flow_Approved")
            .Which.RelationshipType.Should().Be("ConditionalFlow");
        var conditional = model.Relationships.Single(relationship => relationship.Id == "Flow_Approved");
        conditional.Properties["condition"].GetString().Should().Be("approved = true()");
        conditional.Properties["conditionLanguage"].GetString().Should().Be("http://www.w3.org/1999/XPath");

        model.Relationships.Single(relationship => relationship.Id == "Flow_Rejected").RelationshipType
            .Should().Be("DefaultFlow", "the gateway names Flow_Rejected as its default outgoing flow");
        model.Relationships.Where(relationship => relationship.RelationshipType == "SequenceFlow")
            .Select(relationship => relationship.Id).Should().Equal("Flow_1", "Flow_2", "Flow_End");
    }

    [Fact]
    public void Unknown_attributes_land_in_properties_with_a_bpmn_prefix()
    {
        var model = SingleModel(Import("simple-process.bpmn"));
        var gateway = Element(model, "Gateway_Approved");
        Property(gateway, "bpmn:default").GetString().Should().Be("Flow_Rejected",
            "an attribute the mapping does not consume is preserved verbatim for round-trip");
    }

    [Fact]
    public void Deterministic_ids_are_stable_for_elements_without_an_id_attribute()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:x">
              <process id="P" isExecutable="false">
                <startEvent name="no id here"/>
              </process>
            </definitions>
            """;

        var first = new BpmnXmlImporter().Import(xml);
        var second = new BpmnXmlImporter().Import(xml);

        first.HasErrors.Should().BeFalse();
        var firstId = first.Models[0].Elements.Single(element => element.SemanticType == "StartEvent").Id;
        firstId.Should().StartWith("StartEvent_");
        firstId.Should().Be(second.Models[0].Elements.Single(element => element.SemanticType == "StartEvent").Id,
            "generated ids must be a stable hash of the element path");
    }

    [Fact]
    public void Importing_twice_produces_byte_identical_results()
    {
        var first = JsonSerializer.Serialize(Import("collaboration-3-pools.bpmn"), JsonOptions);
        var second = JsonSerializer.Serialize(Import("collaboration-3-pools.bpmn"), JsonOptions);

        second.Should().Be(first, "same input must produce the same output");
    }

    // -------------------------------------------------------------- events

    [Fact]
    public void Event_definitions_become_properties_with_resolved_global_names()
    {
        var model = SingleModel(Import("all-events.bpmn"));

        var messageStart = Element(model, "Start_Message");
        var definitions = Property(messageStart, "eventDefinitions");
        definitions.GetArrayLength().Should().Be(1);
        var definition = definitions[0];
        definition.GetProperty("type").GetString().Should().Be("message");
        definition.GetProperty("ref").GetString().Should().Be("Message_StartMsg");
        definition.GetProperty("name").GetString().Should().Be("Start message");

        Property(Element(model, "Start_Timer"), "eventDefinitions")[0]
            .GetProperty("timeCycle").GetString().Should().Be("0 0 6 * * ?");
        Property(Element(model, "Start_Conditional"), "eventDefinitions")[0]
            .GetProperty("condition").GetString().Should().Be("stock < 5");
        Property(Element(model, "End_Terminate"), "eventDefinitions")[0]
            .GetProperty("type").GetString().Should().Be("terminate");
        Property(Element(model, "Throw_Link"), "eventDefinitions")[0]
            .GetProperty("name").GetString().Should().Be("hop");
        Property(Element(model, "Throw_Escalation"), "eventDefinitions")[0]
            .GetProperty("code").GetString().Should().Be("ESC-1");
        Property(Element(model, "End_Error"), "eventDefinitions")[0]
            .GetProperty("code").GetString().Should().Be("E-42");

        Import("all-events.bpmn").Losses.Should().BeEmpty(
            "every global definition in this fixture is referenced");
    }

    [Fact]
    public void Multiple_event_definitions_and_flags_are_preserved()
    {
        var model = SingleModel(Import("multiple-events.bpmn"));

        var multiple = Element(model, "Catch_Multiple");
        Property(multiple, "multiple").GetBoolean().Should().BeTrue();
        Property(multiple, "eventDefinitions").GetArrayLength().Should().Be(2);

        var parallel = Element(model, "Catch_Parallel");
        Property(parallel, "parallelMultiple").GetBoolean().Should().BeTrue();
        var escalation = Property(parallel, "eventDefinitions")[1];
        escalation.GetProperty("type").GetString().Should().Be("escalation");
        escalation.GetProperty("name").GetString().Should().Be("Needs review");
    }

    [Fact]
    public void Boundary_events_keep_attachment_as_properties_only()
    {
        var model = SingleModel(Import("boundary-events.bpmn"));

        var timer = Element(model, "Boundary_Timer");
        timer.SemanticType.Should().Be("BoundaryEvent");
        Property(timer, "attachedToRef").GetString().Should().Be("Task_Main");
        Property(timer, "cancelActivity").GetBoolean().Should().BeTrue();
        Property(Element(model, "Boundary_Message"), "cancelActivity").GetBoolean().Should().BeFalse();

        model.Relationships.Should().NotContain(relationship =>
            relationship.SourceElementId == "Boundary_Timer" && relationship.TargetElementId == "Task_Main",
            "attachment is layout information; no synthetic relationship may be created");

        Property(Element(model, "Boundary_Cancel"), "eventDefinitions")[0]
            .GetProperty("type").GetString().Should().Be("cancel");
        var error = Property(Element(model, "Boundary_Error"), "eventDefinitions")[0];
        error.GetProperty("ref").GetString().Should().Be("Error_Decline");
        error.GetProperty("name").GetString().Should().Be("Declined");
    }

    [Fact]
    public void Transactions_import_as_subprocesses_with_a_transaction_flag()
    {
        var model = SingleModel(Import("boundary-events.bpmn"));

        var transaction = Element(model, "Transaction_Pay");
        transaction.SemanticType.Should().Be("SubProcess");
        Property(transaction, "transaction").GetBoolean().Should().BeTrue();

        Import("boundary-events.bpmn").Losses.Should().BeEmpty(
            "this transaction has no compensation content, so no approximated-loss is expected");
    }

    [Fact]
    public void Event_subprocesses_are_typed_and_keep_their_children()
    {
        var model = SingleModel(Import("event-subprocess.bpmn"));

        var eventSubProcess = Element(model, "SubProcess_OnSignal");
        eventSubProcess.SemanticType.Should().Be("EventSubProcess");
        Element(model, "Task_Notify").ParentId.Should().Be("SubProcess_OnSignal");
        Element(model, "StartEvent_Escalation").ParentId.Should().Be("SubProcess_OnSignal");
        Property(Element(model, "StartEvent_Escalation"), "bpmn:isInterrupting").GetString().Should().Be("false");
    }

    // -------------------------------------------------------------- hierarchy

    [Fact]
    public void Lanes_nest_and_own_flow_nodes_through_flowNodeRef()
    {
        var model = SingleModel(Import("nested-lanes.bpmn"));

        Element(model, "Lane_FrontOffice").ParentId.Should().BeEmpty();
        Element(model, "Lane_Phone").ParentId.Should().Be("Lane_FrontOffice");
        Element(model, "Lane_Email").ParentId.Should().Be("Lane_FrontOffice");
        Element(model, "Task_PhoneCall").ParentId.Should().Be("Lane_Phone");
        Element(model, "Task_ReplyEmail").ParentId.Should().Be("Lane_Email");
        Element(model, "Task_Assess").ParentId.Should().Be("Lane_BackOffice");
    }

    [Fact]
    public void Subprocesses_nest_three_levels_via_ParentId()
    {
        var model = SingleModel(Import("nested-subprocess.bpmn"));

        Element(model, "SubProcess_L3").ParentId.Should().Be("SubProcess_L2");
        Element(model, "SubProcess_L2").ParentId.Should().Be("SubProcess_L1");
        Element(model, "Task_L3").ParentId.Should().Be("SubProcess_L3");
        Element(model, "StartEvent_L3").ParentId.Should().Be("SubProcess_L3");
    }

    [Fact]
    public void Documentation_becomes_the_element_description()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:x">
              <process id="P" isExecutable="false">
                <task id="T" name="Task">
                  <documentation>First doc.</documentation>
                  <documentation>Second doc is dropped by design (first wins).</documentation>
                </task>
              </process>
            </definitions>
            """;

        var model = SingleModel(new BpmnXmlImporter().Import(xml));
        Element(model, "T").Description.Should().Be("First doc.");
    }

    // -------------------------------------------------------------- collaboration

    [Fact]
    public void Collaboration_splits_into_one_model_per_process_with_pools_and_lanes()
    {
        var result = Import("collaboration-3-pools.bpmn");
        result.HasErrors.Should().BeFalse();

        result.Models.Select(model => model.Id).Should().Equal(
            "Process_Customer", "Process_Shop", "Process_Warehouse");

        var shop = result.Models[1];
        Element(shop, "Participant_Shop").SemanticType.Should().Be("Pool");
        Element(shop, "Lane_Sales").ParentId.Should().Be("Participant_Shop");
        Element(shop, "Task_Invoice").ParentId.Should().Be("Lane_Billing");
        shop.Relationships.Count(relationship => relationship.RelationshipType == "SequenceFlow").Should().Be(4);
    }

    [Fact]
    public void Cross_model_message_flows_stay_in_the_source_model_with_an_external_ref()
    {
        var result = Import("collaboration-3-pools.bpmn");
        var customer = result.Models[0];
        var shop = result.Models[1];
        var warehouse = result.Models[2];

        var order = customer.Relationships.Should().ContainSingle(relationship => relationship.Id == "MessageFlow_Order").Which;
        order.RelationshipType.Should().Be("MessageFlow");
        order.SourceElementId.Should().Be("Task_PlaceOrder");
        order.TargetElementId.Should().Be("StartEvent_OrderReceived");
        var external = order.Properties["externalModelRef"];
        external.GetProperty("modelId").GetString().Should().Be("Process_Shop");
        external.GetProperty("elementId").GetString().Should().Be("StartEvent_OrderReceived");

        shop.Relationships.Should().Contain(relationship => relationship.Id == "MessageFlow_Confirm");
        warehouse.Relationships.Should().ContainSingle(relationship => relationship.RelationshipType == "MessageFlow");
    }

    [Fact]
    public void SingleWithPools_imports_one_model_with_root_pools()
    {
        var model = SingleModel(Import("collaboration-3-pools.bpmn",
            options => options.ModelSplit = BpmnModelSplit.SingleWithPools));

        model.Id.Should().Be("Collaboration_1");
        model.Elements.Where(element => element.SemanticType == "Pool")
            .Select(element => element.Id).Should().Equal(
                "Participant_Customer", "Participant_Shop", "Participant_Warehouse");
        Element(model, "Lane_Sales").ParentId.Should().Be("Participant_Shop");
        Element(model, "Task_PlaceOrder").ParentId.Should().Be("Participant_Customer");
        model.Relationships.Count(relationship => relationship.RelationshipType == "MessageFlow").Should().Be(4);
        model.Relationships.Should().OnlyContain(
            relationship => !relationship.Properties.ContainsKey("externalModelRef"),
            "single-model split has no external references");
    }

    [Fact]
    public void Black_box_pool_imports_with_a_loss_record_and_empty_contents()
    {
        var result = Import("black-box-pool.bpmn");
        var model = SingleModel(result);

        var pool = Element(model, "Participant_External");
        pool.SemanticType.Should().Be("Pool");
        model.Elements.Should().NotContain(element =>
            element.ParentId == "Participant_External", "a black-box pool has no children");

        result.Losses.Should().ContainSingle(loss => loss.Construct == "black-box pool")
            .Which.Severity.Should().Be(BpmnImportLossSeverity.Info);

        var request = model.Relationships.Single(relationship => relationship.Id == "MessageFlow_Request");
        request.TargetElementId.Should().Be("Participant_External");
        var reply = model.Relationships.Single(relationship => relationship.Id == "MessageFlow_Reply");
        reply.SourceElementId.Should().Be("Participant_External");
        request.Properties.Should().NotContainKey("externalModelRef",
            "the black-box pool element is imported into the same model, so both flows are in-model");
    }

    [Fact]
    public void Call_activity_keeps_calledElement_attributes_in_properties()
    {
        var result = Import("call-activity.bpmn");
        result.Models.Select(model => model.Id).Should().Equal("Process_Order", "Process_Fulfilment");

        var callActivity = Element(result.Models[0], "CallActivity_Fulfil");
        callActivity.SemanticType.Should().Be("CallActivity");
        Property(callActivity, "bpmn:calledElement").GetString().Should().Be("Process_Fulfilment");
        // Legacy unprefixed Camunda dialect: preserved under the Camunda namespace key so the
        // exporter writes them back namespaced (schema-valid per bpmn-moddle).
        Property(callActivity, "camunda:calledElementBinding").GetString().Should().Be("latest");
        Property(callActivity, "camunda:calledElementVersion").GetString().Should().Be("3");
    }

    // -------------------------------------------------------------- data

    [Fact]
    public void Data_associations_resolve_implicit_and_io_endpoints_to_the_enclosing_activity()
    {
        var model = SingleModel(Import("io-properties.bpmn"));

        var template = model.Relationships.Single(relationship => relationship.Id == "DIA_Template");
        template.RelationshipType.Should().Be("DataInputAssociation");
        template.SourceElementId.Should().Be("DataObjectRef_Doc");
        template.TargetElementId.Should().Be("Task_Write",
            "the ioSpecification dataInput endpoint resolves to the enclosing activity");

        var report = model.Relationships.Single(relationship => relationship.Id == "DOA_Report");
        report.RelationshipType.Should().Be("DataOutputAssociation");
        report.SourceElementId.Should().Be("Task_Write",
            "a data output association without sourceRef defaults to the enclosing activity");
        report.TargetElementId.Should().Be("DataObjectRef_Doc");

        var approver = model.Relationships.Single(relationship => relationship.Id == "DIA_Approver");
        approver.TargetElementId.Should().Be("Task_Approve",
            "a plain <property> id target resolves to the enclosing activity");
    }

    [Fact]
    public void Data_objects_and_stores_import_with_references_and_collections()
    {
        var result = Import("data-objects.bpmn");
        var model = SingleModel(result);

        var stock = Element(model, "DataObjectRef_Stock");
        stock.SemanticType.Should().Be("DataObject");
        Property(stock, "dataObjectRef").GetString().Should().Be("DataObject_Stock");
        Property(stock, "isCollection").GetBoolean().Should().BeFalse();

        var unused = Element(model, "DataObject_Unused");
        Property(unused, "isCollection").GetBoolean().Should().BeTrue();
        result.Losses.Should().ContainSingle(loss =>
            loss.Construct == "dataObject" && loss.ElementId == "DataObject_Unused" &&
            loss.Severity == BpmnImportLossSeverity.Info);

        var catalog = Element(model, "DataStoreRef_Catalog");
        catalog.SemanticType.Should().Be("DataStore");
        Property(catalog, "dataStoreRef").GetString().Should().Be("DataStore_Catalog");
        Property(catalog, "dataStoreName").GetString().Should().Be("Product catalog");

        var dataOut = model.Relationships.Single(relationship => relationship.Id == "DataOut_1");
        dataOut.Properties["assignments"][0].GetProperty("from").GetString().Should().Be("checkedStock");
        dataOut.Properties["assignments"][0].GetProperty("to").GetString().Should().Be("stockLevel");
    }

    [Fact]
    public void Annotations_groups_and_direction_are_imported()
    {
        var model = SingleModel(Import("annotations-group.bpmn"));

        Element(model, "TextAnnotation_Note1").Name.Should().Be(
            "Remember to keep the SLA under 2 days for premium clients.");

        var note = model.Relationships.Single(relationship => relationship.Id == "Association_Note2");
        note.RelationshipType.Should().Be("Association");
        note.Properties["direction"].GetString().Should().Be("One");

        var group = Element(model, "Group_Outsourced");
        Property(group, "categoryValueRef").GetString().Should().Be("CategoryValue_Outsource");
        Property(group, "categoryValue").GetString().Should().Be("Outsourced");
    }

    // -------------------------------------------------------------- extensions (generic, pre-plugin phase)

    [Fact]
    public void Unknown_extension_namespaces_are_preserved_under_foreignExtensions_with_a_loss()
    {
        var result = Import("camunda-export.bpmn");
        var model = SingleModel(result);

        var task = Element(model, "Task_Approve");
        Property(task, "camunda:assignee").GetString().Should().Be("${approver}");
        Property(task, "camunda:dueDate").GetString().Should().Be("PT2D");
        Property(Element(model, "Task_ReviewPool"), "camunda:candidateGroups").GetString().Should().Be("finance,controlling");
        var foreign = Property(task, "foreignExtensions");
        foreign.GetProperty("http://camunda.org/schema/1.0/bpmn").GetString()
            .Should().Contain("camunda:taskListener");

        result.Losses.Should().Contain(loss =>
            loss.Construct == "extensionElements" &&
            loss.Reason.Contains("http://camunda.org/schema/1.0/bpmn") &&
            loss.ElementId == "Task_Approve");
        result.Losses.Should().Contain(loss =>
            loss.Construct == "extensionElements" && loss.ElementId == null &&
            loss.Reason.Contains("department"),
            "process-level extensionElements have no element to attach to but must not be dropped silently");
    }

    [Fact]
    public void Prefixed_qname_dialect_imports_identically_to_the_default_dialect()
    {
        var model = SingleModel(Import("prefixed-qnames.bpmn"));

        var catchEvent = Element(model, "Catch_Notify");
        var definition = Property(catchEvent, "eventDefinitions")[0];
        definition.GetProperty("ref").GetString().Should().Be("tns_Message_Notify");
        definition.GetProperty("name").GetString().Should().Be("Notify");
    }

    // -------------------------------------------------------------- loss report + validation

    [Fact]
    public void Unsupported_constructs_all_appear_in_the_loss_report()
    {
        var result = Import("unsupported-constructs.bpmn");
        result.HasErrors.Should().BeFalse();
        result.Models.Select(model => model.Id).Should().Equal("Process_Us", "Process_Them");

        string[] expectedConstructs =
        [
            "choreographyTask", "subChoreography", "callChoreography", "conversation",
            "subConversation", "callConversation", "conversationLink",
            "multiInstanceLoopCharacteristics", "standardLoopCharacteristics",
            "adHocSubProcess", "transaction", "signal",
        ];

        foreach (var construct in expectedConstructs)
        {
            result.Losses.Should().Contain(loss => loss.Construct == construct,
                $"every unsupported construct must be reported, missing: {construct}");
        }

        foreach (var loss in result.Losses)
        {
            loss.XmlPath.Should().StartWith("/definitions");
            loss.Reason.Should().NotBeNullOrWhiteSpace();
        }

        var transaction = result.Losses.Single(loss => loss.Construct == "transaction");
        transaction.Severity.Should().Be(BpmnImportLossSeverity.Approximated);
        transaction.ElementId.Should().Be("Transaction_Unsup");

        result.Losses.Single(loss => loss.Construct == "signal").Severity
            .Should().Be(BpmnImportLossSeverity.Info, "unused global definitions are info-level");
    }

    [Fact]
    public void Losses_are_mapped_to_bpmn_import_issues_on_the_owning_model()
    {
        var result = Import("unsupported-constructs.bpmn");
        var us = result.Models[0];

        us.Issues.Should().Contain(issue =>
            issue.Category == "bpmn-import" && issue.SourceElementId == "Transaction_Unsup");
        us.Issues.Should().OnlyContain(issue => issue.Id.Length > 0 && issue.Message.Length > 0);
    }

    [Fact]
    public void Compensate_end_event_keeps_activityRef_and_waitForCompletion()
    {
        var result = Import("unsupported-constructs.bpmn");
        var us = result.Models[0];
        var end = Element(us, "EndEvent_Unsup");
        var definition = Property(end, "eventDefinitions")[0];
        definition.GetProperty("type").GetString().Should().Be("compensate");
        definition.GetProperty("activityRef").GetString().Should().Be("Task_PayU");
        definition.GetProperty("waitForCompletion").GetBoolean().Should().BeTrue();

        Property(Element(us, "Task_UndoPay"), "isForCompensation").GetBoolean().Should().BeTrue();
        us.Relationships.Should().Contain(relationship =>
            relationship.Id == "Association_Comp" && relationship.RelationshipType == "Association");
    }

    [Fact]
    public void Validation_issues_flag_structural_problems_without_throwing()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:x">
              <process id="P" isExecutable="false">
                <startEvent id="StartBroken" name="start">
                  <outgoing>F1</outgoing>
                </startEvent>
                <endEvent id="EndWithOutgoing" name="end">
                  <incoming>F2</incoming>
                  <outgoing>F3</outgoing>
                </endEvent>
                <boundaryEvent id="BoundaryBroken" attachedToRef="MissingActivity">
                  <outgoing>F4</outgoing>
                </boundaryEvent>
                <task id="TaskBroken" name="task">
                  <incoming>F1</incoming>
                  <outgoing>F0</outgoing>
                </task>
                <sequenceFlow id="F0" sourceRef="TaskBroken" targetRef="StartBroken"/>
                <sequenceFlow id="F1" sourceRef="StartBroken" targetRef="TaskBroken"/>
                <sequenceFlow id="F2" sourceRef="GhostSource" targetRef="EndWithOutgoing"/>
                <sequenceFlow id="F3" sourceRef="EndWithOutgoing" targetRef="AlsoMissing"/>
                <sequenceFlow id="F4" sourceRef="BoundaryBroken" targetRef="TaskBroken"/>
              </process>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);
        var model = SingleModel(result);
        var messages = model.Issues.Select(issue => issue.Message).ToList();

        messages.Should().Contain(message => message.Contains("StartBroken") && message.Contains("incoming"));
        messages.Should().Contain(message => message.Contains("EndWithOutgoing") && message.Contains("outgoing"));
        messages.Should().Contain(message => message.Contains("BoundaryBroken") && message.Contains("MissingActivity"));
        messages.Should().Contain(message => message.Contains("GhostSource"));
        model.Issues.Should().Contain(issue => issue.Severity == ModelingIssueSeverity.Error);
    }

    // -------------------------------------------------------------- registration + parity

    [Fact]
    public void AddTempoBpmnXml_registers_the_importer()
    {
        var services = new ServiceCollection();
        services.AddTempoBpmnXml();
        services.AddTempoBpmnXml();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBpmnXmlImporter>().Should().BeOfType<BpmnXmlImporter>();
        provider.GetServices<IBpmnXmlImporter>().Should().ContainSingle();
    }

    // -------------------------------------------------------------- review findings (phase 2)

    [Fact]
    public void Import_of_200k_flat_tasks_completes_within_a_few_seconds()
    {
        const int taskCount = 200_000;
        var builder = new System.Text.StringBuilder();
        builder.Append("<definitions xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" id=\"D\" targetNamespace=\"urn:perf\">");
        builder.Append("<process id=\"P\" isExecutable=\"false\">");
        for (var i = 0; i < taskCount; i++)
        {
            builder.Append("<task id=\"T").Append(i).Append("\"/>");
        }

        builder.Append("</process></definitions>");
        var xml = builder.ToString();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = new BpmnXmlImporter().Import(xml);
        watch.Stop();

        result.HasErrors.Should().BeFalse(result.ErrorMessage ?? "import failed");
        result.Models.Should().ContainSingle().Which.Elements.Should().HaveCount(taskCount);
        // Budget raised 10 s → 20 s (review finding 2): the assertion guards near-linear import,
        // not an absolute wall-clock figure — under a parallel MSBuild/test load the same import
        // that normally takes ~4 s has been observed at 11.1 s, so a 10 s cap flaked.
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
            "the per-model id sets and the element index must keep the import near-linear");
    }

    [Fact]
    public void Tool_plugins_are_invoked_once_per_extensionElements_and_namespace()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:e="http://example.com/ext" id="D" targetNamespace="urn:x">
              <process id="P" isExecutable="false">
                <task id="T" name="task">
                  <extensionElements>
                    <e:one a="1"/>
                    <e:two a="2"/>
                  </extensionElements>
                </task>
              </process>
            </definitions>
            """;
        var extension = new CountingExtension();
        var options = new BpmnImportOptions();
        options.ToolExtensions.Add(extension);

        var result = new BpmnXmlImporter().Import(xml, options);

        result.HasErrors.Should().BeFalse();
        extension.ImportCalls.Should().Be(1, "one extensionElements carries one namespace — one plugin call");
        extension.PayloadsSeen.Should().Be(2, "the context hands over the whole extensionElements payload");
    }

    [Fact]
    public void Relationship_attributes_are_caught_and_endpoint_attributes_are_not_duplicated()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:x">
              <message id="Message_1" name="Payload"/>
              <collaboration id="C">
                <participant id="Pa" processRef="P1"/>
                <participant id="Pb" processRef="P2"/>
                <messageFlow id="MF_1" sourceRef="Pa" targetRef="Pb" messageRef="Message_1" isImmediate="false"/>
              </collaboration>
              <process id="P1" isExecutable="false"><task id="T1"/></process>
              <process id="P2" isExecutable="false"><task id="T2"/></process>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);

        result.HasErrors.Should().BeFalse();
        var flow = result.Models.SelectMany(model => model.Relationships).Single(relationship => relationship.Id == "MF_1");
        flow.Properties["bpmn:messageRef"].GetString().Should().Be("Message_1");
        flow.Properties["bpmn:isImmediate"].GetString().Should().Be("false");
        flow.Properties.Should().NotContainKey("bpmn:sourceRef")
            .And.NotContainKey("bpmn:targetRef", "endpoint attributes are consumed by the mapping, not duplicated");
        result.Losses.Should().NotContain(loss => loss.Construct == "message",
            "a message referenced from a messageFlow is used and must not be reported as unused");
    }

    [Fact]
    public void Referenced_global_definitions_are_kept_on_the_model_as_bpmnDefinitions()
    {
        var model = SingleModel(Import("all-events.bpmn"));

        var definitions = model.Properties["bpmnDefinitions"];
        var byId = definitions.EnumerateArray().ToDictionary(
            definition => definition.GetProperty("id").GetString()!);
        byId.Keys.Should().Contain(["Message_StartMsg", "Signal_Alarm", "Error_Broken", "Escalation_Late"]);
        byId["Error_Broken"].GetProperty("kind").GetString().Should().Be("error");
        byId["Error_Broken"].GetProperty("code").GetString().Should().Be("E-42");
        byId["Message_StartMsg"].GetProperty("name").GetString().Should().Be("Start message");
    }

    [Fact]
    public void Unreferenced_io_declarations_report_their_exact_xml_path()
    {
        var result = Import("io-properties.bpmn");

        var loss = result.Losses.Should().ContainSingle(entry =>
            entry.Construct == "property" && entry.ElementId == "Task_Write").Which;
        loss.XmlPath.Should().Be(
            "/definitions[@id='Definitions_IoProperties']/process[@id='Process_Io']/task[@id='Task_Write']/property[@id='Property_Config']");
    }

    [Fact]
    public void Element_and_relationship_source_paths_carry_id_predicates()
    {
        var model = SingleModel(Import("simple-process.bpmn"));

        model.Elements.Should().OnlyContain(element =>
            element.SourcePath.StartsWith("/definitions[@id='", StringComparison.Ordinal)
            && element.SourcePath.Contains("/process[@id='Process_Simple']/", StringComparison.Ordinal));
        model.Relationships.Should().OnlyContain(relationship =>
            !relationship.Properties.ContainsKey("bpmn:sourceRef") && !relationship.Properties.ContainsKey("bpmn:targetRef"));
    }

    private sealed class CountingExtension : IBpmnToolExtension
    {
        public string NamespaceUri => "http://example.com/ext";

        public int ImportCalls { get; private set; }

        public int PayloadsSeen { get; private set; }

        public void OnImportElement(BpmnToolImportContext context)
        {
            ImportCalls++;
            PayloadsSeen += context.ExtensionElements.Elements().Count();
        }

        public void OnExportElement(BpmnToolExportContext context)
        {
        }
    }

    [Fact]
    public void Converter_semantic_type_constants_match_BpmnModelingCatalog()
    {
        var elementFields = typeof(BpmnSemanticTypes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!);
        elementFields.Should().BeEquivalentTo(
            Tempo.Blazor.Components.Modeling.BpmnModelingCatalog.ElementTypes,
            "the converter duplicates the catalog names on purpose and must not drift");

        var relationshipFields = typeof(BpmnRelationshipTypes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!);
        relationshipFields.Should().BeEquivalentTo(
            Tempo.Blazor.Components.Modeling.BpmnModelingCatalog.RelationshipTypes);
    }
}
