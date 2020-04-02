// Copyright (c) 2020 ALT LLC
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of source code located below and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//  
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//  
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Threading;

using Antilatency;
using Antilatency.DeviceNetwork;
using Antilatency.Alt.Tracking;
using Antilatency.StorageClient;

class Program {
    static void Main(string[] args) {
        var trackignExample = new AltTrackingExample();
        while (true) {
            var node = trackignExample.WaitForNode();
            trackignExample.RunTrackingTask(node);
        }
    }
}

class AltTrackingExample {
    private Antilatency.DeviceNetwork.ILibrary _adnLibrary;
    private Antilatency.DeviceNetwork.INetwork _deviceNetwork;

    private Antilatency.Alt.Tracking.ILibrary _altTrackingLibrary;
    private Antilatency.Alt.Tracking.ITrackingCotask _trackingCotask;
    private Antilatency.Alt.Tracking.IEnvironment _environment;
    private Antilatency.Math.floatP3Q _placement;

    private Antilatency.StorageClient.ILibrary _antilatencyStorageClientLibrary;

    public AltTrackingExample() {
        //Load libraries.
        _adnLibrary = Antilatency.DeviceNetwork.Library.load();
        _antilatencyStorageClientLibrary = Antilatency.StorageClient.Library.load();
        _altTrackingLibrary = Antilatency.Alt.Tracking.Library.load();

        if (_adnLibrary == null) {
            throw new Exception("Failed to load AntilatencyDeviceNetwork library");
        }

        if (_antilatencyStorageClientLibrary == null) {
            throw new Exception("Failed to load AntilatencyStorageClient library");
        }

        if (_altTrackingLibrary == null) {
            throw new Exception("Failed to load AntilatencyAltTracking library");
        }

        //Set log verbosity level for Antilatency Device Network library.
        _adnLibrary.setLogLevel(LogLevel.Info);

        Console.WriteLine("Antilatency Device Network version: " + _adnLibrary.getVersion());

        //Create Antilatency Device Network.
        _deviceNetwork = _adnLibrary.createNetwork(new[] { new UsbDeviceType { vid = UsbVendorId.Antilatency, pid = 0x0000 } });

        //Read default environment code from AntilatencyService.
        var environmentCode = _antilatencyStorageClientLibrary.getLocalStorage().read("environment", "default");

        //Read default placement code from AntilatencyService.
        var placementCode = _antilatencyStorageClientLibrary.getLocalStorage().read("placement", "default");

        //Create placement using code received from storage.
        _placement = new Antilatency.Math.floatP3Q();
        if (string.IsNullOrEmpty(placementCode)) {
            Console.WriteLine("Failed to get placement code, using identity placement");
            _placement.position.x = 0;
            _placement.position.y = 0;
            _placement.position.z = 0;

            _placement.rotation.x = 0;
            _placement.rotation.y = 0;
            _placement.rotation.z = 0;
            _placement.rotation.w = 1;
        } else {
            _placement = _altTrackingLibrary.createPlacement(placementCode);
        }

        Console.WriteLine(
                string.Format("Placement offset: {0}, {1}, {2}, rotation: {3}, {4}, {5}, {6}",
                _placement.position.x, _placement.position.y, _placement.position.z,
                _placement.rotation.x, _placement.rotation.y, _placement.rotation.z, _placement.rotation.w
                )
            );

        //Create environment using code received from storage.
        _environment = _altTrackingLibrary.createEnvironment(environmentCode);

        //Get all tracking markers from environment. For flexible environments markers position will be initialized with some default values
        //and then tracking will correct positions to match markers as close as possible to real positions.
        var markers = _environment.getMarkers();
        for (var i = 0; i < markers.Length; ++i) {
            Console.WriteLine(string.Format("Environment marker position: ({0}, {1}, {2})", markers[i].x, markers[i].y, markers[i].z));
        }
    }

    /// <summary>
    /// Checks if any idle tracking node exists.
    /// </summary>
    /// <returns>First idle tracking node.</returns>
    public Antilatency.DeviceNetwork.NodeHandle WaitForNode() {
        Console.WriteLine("Waiting for tracking node...");

        var node = new NodeHandle();
        var networkUpdateId = 0u;
        do {
            //Every time any node is connected, disconnected or node status is changed, network update id is incremented.
            var updateId = _deviceNetwork.getUpdateId();
            if (networkUpdateId != updateId) {
                networkUpdateId = updateId;

                Console.WriteLine("Network update id has been incremented, searching for available tracking node...");

                node = GetTrackingNode();

                if (node == Antilatency.DeviceNetwork.NodeHandle.Null) {
                    Console.WriteLine("Tracking node not found.");
                }
            }
        } while (node == Antilatency.DeviceNetwork.NodeHandle.Null);

        Console.WriteLine("Tracking node found, serial number: " + _deviceNetwork.nodeGetStringProperty(node, Antilatency.DeviceNetwork.Interop.Constants.HardwareSerialNumberKey));

        return node;
    }

    /// <summary>
    /// Returns the first idle alt tracker node just for demonstration purposes.
    /// </summary>
    private Antilatency.DeviceNetwork.NodeHandle GetTrackingNode() {
        var result = new NodeHandle();

        using (var trackingConstructor = _altTrackingLibrary.createTrackingCotaskConstructor()) {
            //Get all nodes that support tracking task.
            var nodes = trackingConstructor.findSupportedNodes(_deviceNetwork);
            foreach (var node in nodes) {
                //If node status is idle (no task currently running) then we can start tracking task on this node.
                if (_deviceNetwork.nodeGetStatus(node) == NodeStatus.Idle) {
                    result = node;
                    break;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Start tracking task on node and print tracking data while node is connected and task has not been stopped.
    /// </summary>
    /// <param name="node">Node to start tracking on.</param>
    public void RunTrackingTask(Antilatency.DeviceNetwork.NodeHandle node) {
        //Create tracking cotask (run tracking on node).
        _trackingCotask = _altTrackingLibrary.createTrackingCotaskConstructor().startTask(_deviceNetwork, node, _environment);

        while (!_trackingCotask.isTaskFinished()) {
            //Get raw tracker state without extrapolation and placement correction
            var rawState = _trackingCotask.getState(Antilatency.Alt.Tracking.Constants.DefaultAngularVelocityAvgTime);
            Console.WriteLine(string.Format("Raw tracker position: ({0}, {1}, {2})", rawState.pose.position.x, rawState.pose.position.y, rawState.pose.position.z));

            //Get extrapolated tracker state with placement correction
            var extrapolatedState = _trackingCotask.getExtrapolatedState(_placement, 0.06f);
            Console.WriteLine(string.Format("Extrapolated tracker position: ({0}, {1}, {2})", extrapolatedState.pose.position.x, extrapolatedState.pose.position.y, extrapolatedState.pose.position.z));

            //Get current tracking stability stage
            Console.WriteLine("Current tracking stage: " + extrapolatedState.stability.stage);

            //5 FPS pose printing
            Thread.Sleep(200);
        }

        StopTracking();
    }

    /// <summary>
    /// Stop tracking task.
    /// </summary>
    private void StopTracking() {
        Antilatency.Utils.SafeDispose(ref _trackingCotask);
    }

    /// <summary>
    /// Cleanup at object destroy.
    /// </summary>
    ~AltTrackingExample() {
        StopTracking();

        Antilatency.Utils.SafeDispose(ref _altTrackingLibrary);
        Antilatency.Utils.SafeDispose(ref _antilatencyStorageClientLibrary);
        Antilatency.Utils.SafeDispose(ref _deviceNetwork);
        Antilatency.Utils.SafeDispose(ref _adnLibrary);
    }
}
