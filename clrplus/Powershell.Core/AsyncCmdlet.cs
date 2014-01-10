namespace ClrPlus.Powershell.Core {
    using System;
    using System.Collections.Concurrent;
    using System.Management.Automation;
    using System.Threading.Tasks;
    using ClrPlus.Core.Extensions;
    using ClrPlus.Core.Tasks;

    public class BaseCmdlet : PSCmdlet {

        [Parameter(HelpMessage = "Suppress output of all warnings")]
        public SwitchParameter NoWarnings { get; set; }

        [Parameter(HelpMessage = "Suppress output of all non-essential messages")]
        public SwitchParameter Quiet { get; set; }

        protected LocalEventSource LocalEventSource {
            get {
                var local = CurrentTask.Local;

                local.Events += new Error((code, message, objects) => {
                    Host.UI.WriteErrorLine("{0}:{1}".format(code, message.format(objects)));
                    return true;
                });

                if (!NoWarnings && !Quiet) {
                    local.Events += new Warning((code, message, objects) => {
                        WriteWarning("{0}:{1}".format(code, message.format(objects)));
                        return false;
                    });
                }

                local.Events += new Debug((code, message, objects) => {
                    WriteDebug("{0}:{1}".format(code, message.format(objects)));
                    return false;
                });

                local.Events += new Verbose((code, message, objects) => {
                    WriteVerbose("{0}:{1}".format(code, message.format(objects)));
                    return false;
                });

                local.Events += new Progress((code, progress, message, objects) => {
                    WriteProgress(new ProgressRecord(0, code, message.format(objects)) {
                        PercentComplete = progress
                    });
                    return false;
                });

                local.Events += new OutputObject(obj => {
                    if (obj != null) {
                        WriteObject(obj);
                    }
                    return false;
                });

                if (!Quiet) {
                    local.Events += new Message((code, message, objects) => {
                        Host.UI.WriteLine("{0}{1}".format(code, message.format(objects)));
                        return false;
                    });
                }
                return local;
            }
        }


        private string _originalDir;
        protected override void BeginProcessing() {
            _originalDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = (SessionState.PSVariable.GetValue("pwd") ?? "").ToString();
        }

        protected override void EndProcessing() {
            if (_originalDir != null) {
                Environment.CurrentDirectory = _originalDir;
            }
        }
    }

    public abstract class AsyncCmdlet : PSCmdlet {
        private BlockingCollection<Action> _messages;

        public virtual void BeginProcessingAsync() {
        }

        public virtual void EndProcessingAsync() {
        }

        public virtual void ProcessRecordAsync() {
        }

        private void ProcessMessages() {
            foreach (var m in _messages.GetConsumingEnumerable()) {
                m();
            }
        }

        protected override void BeginProcessing() {
            SetupMessages();
            Task.Factory.StartNew(() => {
                BeginProcessingAsync();
                EndLoop();
            }, TaskCreationOptions.LongRunning);
            ProcessMessages();
        }

        protected override void EndProcessing() {
            SetupMessages();
            Task.Factory.StartNew(() => {
                EndProcessingAsync();
                EndLoop();
            }, TaskCreationOptions.LongRunning);
            ProcessMessages();
        }

        protected override void ProcessRecord() {
            SetupMessages();
            Task.Factory.StartNew(() => {
                ProcessRecordAsync();
                EndLoop();
            },TaskCreationOptions.LongRunning);
            ProcessMessages();
        }

        public new void WriteObject(object obj) {
            _messages.Add(() => base.WriteObject(obj));
        }

        public new void WriteObject(object sendToPipeline, bool enumerateCollection) {
            _messages.Add(() => base.WriteObject(sendToPipeline, enumerateCollection));
        }

        public new void WriteProgress(ProgressRecord progressRecord) {
            _messages.Add(() => base.WriteProgress(progressRecord));
        }

        public new void WriteWarning(string text) {
            _messages.Add(() => base.WriteWarning(text));
        }

        public new void WriteDebug(string text) {
            _messages.Add(() => base.WriteDebug(text));
        }

        public new void WriteError(ErrorRecord errorRecord) {
            _messages.Add(() => base.WriteError(errorRecord));
        }

        public new void WriteVerbose(string text) {
            _messages.Add(() => base.WriteDebug(text));
        }

        public new bool ShouldContinue(string query, string caption) {
            throw new NotImplementedException();
        }

        public new bool ShouldContinue(string query, string caption, ref bool yesToAll, ref bool noToAll) {
            throw new NotImplementedException();
        }

        public new bool ShouldProcess(string target) {
            throw new NotImplementedException();
        }

        public new bool ShouldProcess(string target, string action) {
            throw new NotImplementedException();
        }

        public new bool ShouldProcess(string verboseDescription, string verboseWarning, string caption) {
            throw new NotImplementedException();
        }

        public new bool ShouldProcess(string verboseDescription, string verboseWarning, string caption, out ShouldProcessReason shouldProcessReason) {
            throw new NotImplementedException();
        }

        private void SetupMessages() {
            _messages = new BlockingCollection<Action>();
        }

        private void EndLoop() {
            _messages.CompleteAdding();
        }
    }
}