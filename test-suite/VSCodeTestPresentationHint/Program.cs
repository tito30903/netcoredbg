using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using NetcoreDbgTest;
using NetcoreDbgTest.VSCode;
using NetcoreDbgTest.Script;

using Newtonsoft.Json;

namespace NetcoreDbgTest.Script
{
    class Context
    {
        public void PrepareStart(string caller_trace)
        {
            InitializeRequest initializeRequest = new InitializeRequest();
            initializeRequest.arguments.clientID = "vscode";
            initializeRequest.arguments.clientName = "Visual Studio Code";
            initializeRequest.arguments.adapterID = "coreclr";
            initializeRequest.arguments.pathFormat = "path";
            initializeRequest.arguments.linesStartAt1 = true;
            initializeRequest.arguments.columnsStartAt1 = true;
            initializeRequest.arguments.supportsVariableType = true;
            initializeRequest.arguments.supportsVariablePaging = true;
            initializeRequest.arguments.supportsRunInTerminalRequest = true;
            initializeRequest.arguments.locale = "en-us";
            Assert.True(VSCodeDebugger.Request(initializeRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            LaunchRequest launchRequest = new LaunchRequest();
            launchRequest.arguments.name = ".NET Core Launch (console) with pipeline";
            launchRequest.arguments.type = "coreclr";
            launchRequest.arguments.preLaunchTask = "build";
            launchRequest.arguments.program = ControlInfo.TargetAssemblyPath;
            launchRequest.arguments.cwd = "";
            launchRequest.arguments.console = "internalConsole";
            launchRequest.arguments.stopAtEntry = true;
            launchRequest.arguments.internalConsoleOptions = "openOnSessionStart";
            launchRequest.arguments.__sessionId = Guid.NewGuid().ToString();
            Assert.True(VSCodeDebugger.Request(launchRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void PrepareEnd(string caller_trace)
        {
            ConfigurationDoneRequest configurationDoneRequest = new ConfigurationDoneRequest();
            Assert.True(VSCodeDebugger.Request(configurationDoneRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasEntryPointHit(string caller_trace)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "entry")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasExit(string caller_trace)
        {
            bool wasExited = false;
            int ?exitCode = null;
            bool wasTerminated = false;

            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "exited")) {
                    wasExited = true;
                    ExitedEvent exitedEvent = JsonConvert.DeserializeObject<ExitedEvent>(resJSON);
                    exitCode = exitedEvent.body.exitCode;
                }
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "terminated")) {
                    wasTerminated = true;
                }
                if (wasExited && exitCode == 0 && wasTerminated)
                    return true;

                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void DebuggerExit(string caller_trace)
        {
            DisconnectRequest disconnectRequest = new DisconnectRequest();
            disconnectRequest.arguments = new DisconnectArguments();
            disconnectRequest.arguments.restart = false;
            Assert.True(VSCodeDebugger.Request(disconnectRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void AddBreakpoint(string caller_trace, string bpName, string Condition = null)
        {
            Breakpoint bp = ControlInfo.Breakpoints[bpName];
            Assert.Equal(BreakpointType.Line, bp.Type, @"__FILE__:__LINE__"+"\n"+caller_trace);
            var lbp = (LineBreakpoint)bp;

            BreakpointSourceName = lbp.FileName;
            BreakpointList.Add(new SourceBreakpoint(lbp.NumLine, Condition));
            BreakpointLines.Add(lbp.NumLine);
        }

        public void SetBreakpoints(string caller_trace)
        {
            SetBreakpointsRequest setBreakpointsRequest = new SetBreakpointsRequest();
            setBreakpointsRequest.arguments.source.name = BreakpointSourceName;
            // NOTE this code works only with one source file
            setBreakpointsRequest.arguments.source.path = ControlInfo.SourceFilesPath;
            setBreakpointsRequest.arguments.lines.AddRange(BreakpointLines);
            setBreakpointsRequest.arguments.breakpoints.AddRange(BreakpointList);
            setBreakpointsRequest.arguments.sourceModified = false;
            Assert.True(VSCodeDebugger.Request(setBreakpointsRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasBreakpointHit(string caller_trace, string bpName)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "breakpoint")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);

            StackTraceRequest stackTraceRequest = new StackTraceRequest();
            stackTraceRequest.arguments.threadId = threadId;
            stackTraceRequest.arguments.startFrame = 0;
            stackTraceRequest.arguments.levels = 20;
            var ret = VSCodeDebugger.Request(stackTraceRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            Breakpoint breakpoint = ControlInfo.Breakpoints[bpName];
            Assert.Equal(BreakpointType.Line, breakpoint.Type, @"__FILE__:__LINE__"+"\n"+caller_trace);
            var lbp = (LineBreakpoint)breakpoint;

            StackTraceResponse stackTraceResponse =
                JsonConvert.DeserializeObject<StackTraceResponse>(ret.ResponseStr);

            if (stackTraceResponse.body.stackFrames[0].line == lbp.NumLine
                && stackTraceResponse.body.stackFrames[0].source.name == lbp.FileName
                // NOTE this code works only with one source file
                && stackTraceResponse.body.stackFrames[0].source.path == ControlInfo.SourceFilesPath)
                return;

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public Int64 DetectFrameId(string caller_trace, string bpName)
        {
            StackTraceRequest stackTraceRequest = new StackTraceRequest();
            stackTraceRequest.arguments.threadId = threadId;
            stackTraceRequest.arguments.startFrame = 0;
            stackTraceRequest.arguments.levels = 20;
            var ret = VSCodeDebugger.Request(stackTraceRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            Breakpoint breakpoint = ControlInfo.Breakpoints[bpName];
            Assert.Equal(BreakpointType.Line, breakpoint.Type, @"__FILE__:__LINE__"+"\n"+caller_trace);
            var lbp = (LineBreakpoint)breakpoint;

            StackTraceResponse stackTraceResponse =
                JsonConvert.DeserializeObject<StackTraceResponse>(ret.ResponseStr);

            if (stackTraceResponse.body.stackFrames[0].line == lbp.NumLine
                && stackTraceResponse.body.stackFrames[0].source.name == lbp.FileName
                // NOTE this code works only with one source file
                && stackTraceResponse.body.stackFrames[0].source.path == ControlInfo.SourceFilesPath)
                return stackTraceResponse.body.stackFrames[0].id;

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public int GetVariablesReference(string caller_trace, Int64 frameId, string ScopeName)
        {
            ScopesRequest scopesRequest = new ScopesRequest();
            scopesRequest.arguments.frameId = frameId;
            var ret = VSCodeDebugger.Request(scopesRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            ScopesResponse scopesResponse =
                JsonConvert.DeserializeObject<ScopesResponse>(ret.ResponseStr);

            foreach (var Scope in scopesResponse.body.scopes) {
                if (Scope.name == ScopeName) {
                    return Scope.variablesReference == null ? 0 : (int)Scope.variablesReference;
                }
            }

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public int GetChildVariablesReference(string caller_trace, int VariablesReference, string VariableName)
        {
            VariablesRequest variablesRequest = new VariablesRequest();
            variablesRequest.arguments.variablesReference = VariablesReference;
            var ret = VSCodeDebugger.Request(variablesRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            VariablesResponse variablesResponse =
                JsonConvert.DeserializeObject<VariablesResponse>(ret.ResponseStr);

            foreach (var Variable in variablesResponse.body.variables) {
                if (Variable.name == VariableName)
                    return Variable.variablesReference;
            }

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void CheckPresentationHint(string caller_trace, int variablesReference,
            string variableName, List<string> expectedAttributes)
        {
            VariablesRequest variablesRequest = new VariablesRequest();
            variablesRequest.arguments.variablesReference = variablesReference;
            var ret = VSCodeDebugger.Request(variablesRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            VariablesResponse variablesResponse =
                JsonConvert.DeserializeObject<VariablesResponse>(ret.ResponseStr);

            foreach (var Variable in variablesResponse.body.variables) {
                if (Variable.name == variableName) {
                    var actualAttributes = Variable.presentationHint?.attributes ?? new List<string>();
                    var sortedExpected = expectedAttributes.OrderBy(x => x).ToList();
                    var sortedActual = actualAttributes.OrderBy(x => x).ToList();

                    Assert.True(sortedExpected.SequenceEqual(sortedActual),
                        @"__FILE__:__LINE__"+"\n"+caller_trace
                        + "\nVariable: " + variableName
                        + "\nExpected attributes: [" + string.Join(", ", sortedExpected) + "]"
                        + "\nActual attributes: [" + string.Join(", ", sortedActual) + "]");
                    return;
                }
            }

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace
                + "\nVariable '" + variableName + "' not found");
        }

        public void CheckLocalPresentationHint(string caller_trace, int variablesReference,
            string variableName, List<string> expectedAttributes)
        {
            VariablesRequest variablesRequest = new VariablesRequest();
            variablesRequest.arguments.variablesReference = variablesReference;
            var ret = VSCodeDebugger.Request(variablesRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            VariablesResponse variablesResponse =
                JsonConvert.DeserializeObject<VariablesResponse>(ret.ResponseStr);

            foreach (var Variable in variablesResponse.body.variables) {
                if (Variable.name == variableName) {
                    var actualAttributes = Variable.presentationHint?.attributes ?? new List<string>();
                    var sortedExpected = expectedAttributes.OrderBy(x => x).ToList();
                    var sortedActual = actualAttributes.OrderBy(x => x).ToList();

                    Assert.True(sortedExpected.SequenceEqual(sortedActual),
                        @"__FILE__:__LINE__"+"\n"+caller_trace
                        + "\nLocal variable: " + variableName
                        + "\nExpected attributes: [" + string.Join(", ", sortedExpected) + "]"
                        + "\nActual attributes: [" + string.Join(", ", sortedActual) + "]");
                    return;
                }
            }

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace
                + "\nLocal variable '" + variableName + "' not found");
        }

        public void Continue(string caller_trace)
        {
            ContinueRequest continueRequest = new ContinueRequest();
            continueRequest.arguments.threadId = threadId;
            Assert.True(VSCodeDebugger.Request(continueRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public Context(ControlInfo controlInfo, NetcoreDbgTestCore.DebuggerClient debuggerClient)
        {
            ControlInfo = controlInfo;
            VSCodeDebugger = new VSCodeDebugger(debuggerClient);
        }

        ControlInfo ControlInfo;
        VSCodeDebugger VSCodeDebugger;
        int threadId = -1;
        // NOTE this code works only with one source file
        string BreakpointSourceName;
        List<SourceBreakpoint> BreakpointList = new List<SourceBreakpoint>();
        List<int> BreakpointLines = new List<int>();
    }
}

namespace VSCodeTestPresentationHint
{
    // Test class with various member types for presentation hint testing
    public class HintTargets
    {
        // Constants (should get: static, constant)
        public const int MyConst = 42;
        public const string MyConstString = "hello";

        // Static fields (should get: static)
        public static int MyStatic = 100;
        public static string MyStaticString = "static_str";

        // Readonly instance fields (should get: readOnly)
        public readonly int MyReadonly = 10;
        public readonly string MyReadonlyString = "readonly_str";

        // Static readonly fields (should get: static, readOnly)
        public static readonly int MyStaticReadonly = 200;
        public static readonly string MyStaticReadonlyString = "static_readonly_str";

        // Regular field (should get no attributes, or rawString for string)
        public int MyField = 5;
        public string MyString = "plain_str";

        // Properties with getter only (should get: readOnly)
        public int GetterOnly { get { return 99; } }
        public string GetterOnlyString { get { return "getter_str"; } }

        // Properties with getter and setter (should get no special attributes, or rawString for string)
        public int GetterSetter { get; set; }
        public string GetterSetterString { get; set; }

        public HintTargets()
        {
            GetterSetter = 77;
            GetterSetterString = "getset_str";
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Label.Checkpoint("init", "test_hints", (Object context) => {
                Context Context = (Context)context;
                Context.PrepareStart(@"__FILE__:__LINE__");
                Context.AddBreakpoint(@"__FILE__:__LINE__", "BREAK1");
                Context.SetBreakpoints(@"__FILE__:__LINE__");
                Context.PrepareEnd(@"__FILE__:__LINE__");
                Context.WasEntryPointHit(@"__FILE__:__LINE__");
                Context.Continue(@"__FILE__:__LINE__");
            });

            HintTargets obj = new HintTargets();
            string localString = "local_str";
            int localInt = 123;

            int dummy = 1;                                      Label.Breakpoint("BREAK1");

            Label.Checkpoint("test_hints", "finish", (Object context) => {
                Context Context = (Context)context;
                Context.WasBreakpointHit(@"__FILE__:__LINE__", "BREAK1");
                Int64 frameId = Context.DetectFrameId(@"__FILE__:__LINE__", "BREAK1");
                int localsRef = Context.GetVariablesReference(@"__FILE__:__LINE__", frameId, "Locals");

                // Check local variables presentation hints
                // Local string should get rawString
                Context.CheckLocalPresentationHint(@"__FILE__:__LINE__", localsRef,
                    "localString", new List<string> { "rawString" });
                // Local int should get no attributes
                Context.CheckLocalPresentationHint(@"__FILE__:__LINE__", localsRef,
                    "localInt", new List<string>());

                // Expand obj to get its members
                int objRef = Context.GetChildVariablesReference(@"__FILE__:__LINE__", localsRef, "obj");

                // Constants: static + constant
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyConst", new List<string> { "static", "constant" });
                // Constant string: static + constant + rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyConstString", new List<string> { "static", "constant", "rawString" });

                // Static fields: static
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyStatic", new List<string> { "static" });
                // Static string field: static + rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyStaticString", new List<string> { "static", "rawString" });

                // Readonly instance fields: readOnly
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyReadonly", new List<string> { "readOnly" });
                // Readonly string field: readOnly + rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyReadonlyString", new List<string> { "readOnly", "rawString" });

                // Static readonly fields: static + readOnly
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyStaticReadonly", new List<string> { "static", "readOnly" });
                // Static readonly string: static + readOnly + rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyStaticReadonlyString", new List<string> { "static", "readOnly", "rawString" });

                // Regular field: no attributes
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyField", new List<string>());
                // Regular string field: rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "MyString", new List<string> { "rawString" });

                // Getter-only property: readOnly
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "GetterOnly", new List<string> { "readOnly" });
                // Getter-only string property: readOnly + rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "GetterOnlyString", new List<string> { "readOnly", "rawString" });

                // Getter+Setter property: no attributes
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "GetterSetter", new List<string>());
                // Getter+Setter string property: rawString
                Context.CheckPresentationHint(@"__FILE__:__LINE__", objRef,
                    "GetterSetterString", new List<string> { "rawString" });

                Context.Continue(@"__FILE__:__LINE__");
            });

            Label.Checkpoint("finish", "", (Object context) => {
                Context Context = (Context)context;
                Context.WasExit(@"__FILE__:__LINE__");
                Context.DebuggerExit(@"__FILE__:__LINE__");
            });
        }
    }
}
