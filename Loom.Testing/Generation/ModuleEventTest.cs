using Loom.Core.Diagnostics;

namespace Loom.Testing.Generation;

/// <summary>
///     An event crossing a module boundary: the exporting module sends its connection store out with the
///     event, so a '+=' in one module and the '-=' that undoes it in another work off the same table.
/// </summary>
[Collection("Assembly")]
public class ModuleEventTest
{
    private const string BusModule = """
        export event message(text: string);
        export fn handler(text: string): void { print(text) }
        """;

    [Fact]
    public void Exports_AnEvent_AlongsideItsConnectionStore() =>
        AssertRendered(
            [("bus.loom", BusModule)],
            "bus.loom",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const _message_connections = {}
            const message: Loom.Event<string> = Loom.Event.new()
            const function handler(text: string): ()
              print(text)
            end
            return { message = message, handler = handler, _message_connections = _message_connections }
            """
        );

    [Fact]
    public void Connects_AnImportedEvent_ThroughTheExportedStore() =>
        AssertRenderedAgainstBus(
            "import { message, handler } from \"./bus\"\nmessage += handler;",
            """
            const bus = require("./bus")
            const message = bus.message
            const handler = bus.handler
            const _message_connections = bus._message_connections
            _message_connections[handler] = message:Connect(handler)
            return nil
            """
        );

    [Fact]
    public void Disconnects_AnImportedEvent_ConnectedByAnotherModule() =>
        AssertRenderedAgainstBus(
            "import { message, handler } from \"./bus\"\nmessage -= handler;",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const bus = require("./bus")
            const message = bus.message
            const handler = bus.handler
            const _message_connections = bus._message_connections
            Loom.disconnect_event(_message_connections, handler)
            return nil
            """
        );

    [Fact]
    public void Reaches_TheStore_ThroughANamespaceImport() =>
        AssertRenderedAgainstBus(
            "import * as bus from \"./bus\"\nbus::message += bus::handler;\nbus::message -= bus::handler;",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const bus = require("./bus")
            const _message_connections = bus._message_connections
            _message_connections[bus.handler] = bus.message:Connect(bus.handler)
            Loom.disconnect_event(_message_connections, bus.handler)
            return nil
            """
        );

    [Fact]
    public void Forwards_TheStore_OfARenamedReExport() =>
        AssertRendered(
            [("bus.loom", BusModule), ("relay.loom", "export { message as broadcast } from \"./bus\"")],
            "relay.loom",
            """
            const bus = require("./bus")
            return { broadcast = bus.message, _broadcast_connections = bus._message_connections }
            """
        );

    [Fact]
    public void Connects_ARenamedImport_ThroughTheSameStore() =>
        AssertRendered(
            [
                ("bus.loom", BusModule),
                ("relay.loom", "export { message as broadcast } from \"./bus\""),
                ("main.loom", "import { broadcast } from \"./relay\"\nimport { handler } from \"./bus\"\nbroadcast += handler;\nbroadcast -= handler;")
            ],
            "main.loom",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const relay = require("./relay")
            const broadcast = relay.broadcast
            const bus = require("./bus")
            const handler = bus.handler
            const _broadcast_connections = relay._broadcast_connections
            _broadcast_connections[handler] = broadcast:Connect(handler)
            Loom.disconnect_event(_broadcast_connections, handler)
            return nil
            """
        );

    /// <summary> An event no other module can see keeps the plain local a '+=' generates on its own. </summary>
    [Fact]
    public void Keeps_ALocalConnection_ForAModuleLocalEvent() =>
        AssertRendered(
            [
                (
                    "bus.loom",
                    """
                    event message(text: string);
                    export fn handler(text: string): void { print(text) }

                    message += handler;
                    message -= handler;
                    """
                )
            ],
            "bus.loom",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const message: Loom.Event<string> = Loom.Event.new()
            const function handler(text: string): ()
              print(text)
            end
            const handler_conn = message:Connect(handler)
            handler_conn:Disconnect()
            return { handler = handler }
            """
        );

    [Fact]
    public void Reports_AnExportedEvent_WhoseStoreNameIsAlreadyExported() =>
        Utility.WithTempProject(
            [("bus.loom", "export event message(text: string);\nexport let _message_connections = 1;")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.EventStoreExportCollision,
                "Exporting event 'message' also exports its connections as '_message_connections', which is already exported.",
                "rename the '_message_connections' export"
            )
        );

    [Fact]
    public void Reports_AGenericEventDeclaration() =>
        Utility.WithTempProject(
            [("bus.loom", "event message<T>(value: T);")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.GenericEventDeclaration,
                "Event declarations cannot be generic.",
                "an event lowers to a single Luau value, and Luau has no way to express a generic value - declare the event without type parameters, or make the enclosing interface generic instead."
            )
        );

    private static void AssertRenderedAgainstBus(string source, string expected) =>
        AssertRendered([("bus.loom", BusModule), ("main.loom", source)], "main.loom", expected);

    private static void AssertRendered((string Path, string Source)[] files, string fileName, string expected) =>
        Utility.WithTempProject(
            files,
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var file = result.Files.Single(compiled => compiled.SourceFile.Name == fileName);
                Assert.Equal(
                    expected.Replace(Environment.NewLine, "\n") + '\n',
                    file.RenderedLuau.Replace(Environment.NewLine, "\n")
                );
            }
        );
}
