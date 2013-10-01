//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2010-2012 Garrett Serack and CoApp Contributors. 
//     Contributors can be discovered using the 'git log' command.
//     All rights reserved.
// </copyright>
// <license>
//     The software is licensed under the Apache 2.0 License (the "License")
//     You may not use the software except in compliance with the License. 
// </license>
//-----------------------------------------------------------------------

namespace CoApp.NuGetNativeMSBuildTasks {
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;

    public class AsyncProcess {
        protected Process _process;
        private AsynchronouslyEnumerableList<string> _stdError = new AsynchronouslyEnumerableList<string>();
        private AsynchronouslyEnumerableList<string> _stdOut = new AsynchronouslyEnumerableList<string>();

        protected AsyncProcess(Process process) {
            _process = process;
        }

        public static AsyncProcess Start(ProcessStartInfo startInfo) {
            return Start(startInfo, null);
        }

        public static AsyncProcess Start(ProcessStartInfo startInfo, IDictionary environment) {
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;

            if (environment != null) {
                foreach (var i in environment.Keys) {
                    startInfo.EnvironmentVariables[(string)i] = (string)environment[i];
                }
            }

            var process = new Process {
                StartInfo = startInfo
            };

            var result = new AsyncProcess(process);

            process.EnableRaisingEvents = true;

            // set up std* access
            process.ErrorDataReceived += (sender, args) => result._stdError.Add(args.Data);
            process.OutputDataReceived += (sender, args) => result._stdOut.Add(args.Data);
            process.Exited += (sender, args) => {

                result._stdError.Completed();
                result._stdOut.Completed();
            };

            process.Start();

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            return result;
        }

        public IEnumerable<string> StandardError {
            get {
                return _stdError;
            }
        }

        public IEnumerable<string> StandardOutput {
            get {
                return _stdOut;
            }
        }

        public static AsyncProcess Start(string fileName) {
            return Start(new ProcessStartInfo {
                FileName = fileName
            });
        }

        public static AsyncProcess Start(string fileName, IDictionary environment) {
            return Start(new ProcessStartInfo {
                FileName = fileName
            }, environment);
        }

        public static AsyncProcess Start(string fileName, string parameters, IDictionary environment) {
            return Start(new ProcessStartInfo {
                FileName = fileName,
                Arguments = parameters
            }, environment);
        }

        public static AsyncProcess Start(string fileName, string parameters) {
            return Start(new ProcessStartInfo {
                FileName = fileName,
                Arguments = parameters
            });
        }


        public void Kill() {
            _process.Kill();
        }

        public bool WaitForExit(int milliseconds) {
            return _process.WaitForExit(milliseconds);
        }

        public void WaitForExit() {
            WaitForExit(-1);
        }

        public bool WaitForInputIdle(int milliseconds) {
            return _process.WaitForExit(milliseconds);
        }

        public bool WaitForInputIdle() {
            return WaitForInputIdle(-1);
        }

        public int ExitCode {
            get {
                return _process.ExitCode;
            }
        }

        public bool HasExited {
            get {
                return _process.HasExited;
            }
        }

        public DateTime ExitTime {
            get {
                return _process.ExitTime;
            }
        }

        public IntPtr Handle {
            get {
                return _process.Handle;
            }
        }

        public int HandleCount {
            get {
                return _process.HandleCount;
            }
        }

        public int Id {
            get {
                return _process.Id;
            }
        }

        public string MachineName {
            get {
                return _process.MachineName;
            }
        }

        public IntPtr MainWindowHandle {
            get {
                return _process.MainWindowHandle;
            }
        }

        public string MainWindowTitle {
            get {
                return _process.MainWindowTitle;
            }
        }

        public ProcessModule MainModule {
            get {
                return _process.MainModule;
            }
        }

        public IntPtr MaxWorkingSet {
            get {
                return _process.MaxWorkingSet;
            }
        }

        public IntPtr MinWorkingSet {
            get {
                return _process.MinWorkingSet;
            }
        }

        public ProcessModuleCollection Modules {
            get {
                return _process.Modules;
            }
        }

        public long NonpagedSystemMemorySize64 {
            get {
                return _process.NonpagedSystemMemorySize64;
            }
        }

        public long PagedMemorySize64 {
            get {
                return _process.PagedMemorySize64;
            }
        }

        public long PagedSystemMemorySize64 {
            get {
                return _process.PagedSystemMemorySize64;
            }
        }

        public long PeakPagedMemorySize64 {
            get {
                return _process.PeakPagedMemorySize64;
            }
        }

        public long PeakWorkingSet64 {
            get {
                return _process.PeakWorkingSet64;
            }
        }

        public long PeakVirtualMemorySize64 {
            get {
                return _process.PeakVirtualMemorySize64;
            }
        }

        public bool PriorityBoostEnabled {
            get {
                return _process.PriorityBoostEnabled;
            }
            set {
                _process.PriorityBoostEnabled = value;
            }
        }

        public ProcessPriorityClass PriorityClass {
            get {
                return _process.PriorityClass;
            }
            set {
                _process.PriorityClass = value;
            }
        }

        public long PrivateMemorySize64 {
            get {
                return _process.PrivateMemorySize64;
            }
        }

        public TimeSpan PrivilegedProcessorTime {
            get {
                return _process.PrivilegedProcessorTime;
            }
        }

        public string ProcessName {
            get {
                return _process.ProcessName;
            }
        }

        public IntPtr ProcessorAffinity {
            get {
                return _process.ProcessorAffinity;
            }
            set {
                _process.ProcessorAffinity = value;
            }
        }

        public bool Responding {
            get {
                return _process.Responding;
            }
        }

        public int SessionId {
            get {
                return _process.SessionId;
            }
        }


        public DateTime StartTime {
            get {
                return _process.StartTime;
            }
        }

        public ISynchronizeInvoke SynchronizingObject {
            get {
                return _process.SynchronizingObject;
            }
        }

        public ProcessThreadCollection Threads {
            get {
                return _process.Threads;
            }
        }

        public TimeSpan TotalProcessorTime {
            get {
                return _process.TotalProcessorTime;
            }
        }

        public TimeSpan UserProcessorTime {
            get {
                return _process.UserProcessorTime;
            }
        }

        public long VirtualMemorySize64 {
            get {
                return _process.VirtualMemorySize64;
            }
        }

        public bool EnableRaisingEvents {
            get {
                return _process.EnableRaisingEvents;
            }
            set {
                _process.EnableRaisingEvents = value;
            }
        }

        public long WorkingSet64 {
            get {
                return _process.WorkingSet64;
            }
        }

        public event EventHandler Exited {
            add {
                _process.Exited += value;
            }
            remove {
                _process.Exited -= value;
            }
        }

        public bool CloseMainWindow() {
            return _process.CloseMainWindow();
        }

        protected void Dispose(bool disposing) {
            if (_process != null) {
                _process.Dispose();
            }
            _process = null;
        }

        public void Close() {
            _process.Close();

        }

        public static void EnterDebugMode() {
            Process.EnterDebugMode();
        }

        public static void LeaveDebugMode() {
            Process.LeaveDebugMode();
        }

        public static AsyncProcess GetProcessById(int processId, string machineName) {
            return new AsyncProcess(Process.GetProcessById(processId, machineName));
        }

        public static AsyncProcess GetProcessById(int processId) {
            return new AsyncProcess(Process.GetProcessById(processId));
        }

        public static AsyncProcess[] GetProcessesByName(string processName) {
            return Process.GetProcessesByName(processName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcessesByName(string processName, string machineName) {
            return Process.GetProcessesByName(processName, machineName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcesses() {
            return Process.GetProcesses().Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess[] GetProcesses(string machineName) {
            return Process.GetProcesses(machineName).Select(each => new AsyncProcess(each)).ToArray();
        }

        public static AsyncProcess GetCurrentProcess() {
            return new AsyncProcess(Process.GetCurrentProcess());
        }

        public void Refresh() {
            _process.Refresh();
        }
    }
}