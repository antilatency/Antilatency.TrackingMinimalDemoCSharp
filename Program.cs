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
using System.Linq;
using System.Threading;

class Program {

    static void Main() {

        using var storageClientLibrary = Antilatency.StorageClient.Library.load();
        using var environmentSelectorLibrary = Antilatency.Alt.Environment.Selector.Library.load();
        using var trackingLibrary = Antilatency.Alt.Tracking.Library.load();
        using var network = CreateNetwork();

        #region CreateTrackingCotaskConstructor
        using var cotaskConstructor = trackingLibrary.createTrackingCotaskConstructor();
        #endregion

        using var environment = CreateEnvironment(storageClientLibrary, environmentSelectorLibrary);
        var placement = CreatePlacement(storageClientLibrary, trackingLibrary);

        Console.WriteLine("----- Settings -----");
        PrintEnvironmentMarkers(environment);
        PrintPlacementInfo(placement);

        while (true) {
            Console.WriteLine("----- Waiting for a tracking node -----");

            var node = WaitForCompatibleNode(cotaskConstructor, network);

            string serialNo = network.nodeGetStringProperty(node,
                    Antilatency.DeviceNetwork.Interop.Constants.HardwareSerialNumberKey);

            Console.WriteLine($"Tracking is about to start on node {node}, s/n {serialNo}");

            #region StartTrackingTask
            using var cotask = cotaskConstructor.startTask(network, node, environment);
            #endregion

            PrintTrackingState(cotask, placement);
        }
    }

    private static Antilatency.DeviceNetwork.INetwork CreateNetwork() {
        using var adnLibrary = Antilatency.DeviceNetwork.Library.load();
        if (adnLibrary == null) {
            throw new Exception("Failed to load AntilatencyDeviceNetwork library");
        }

        Console.WriteLine(
            $"AntilatencyDeviceNetwork version: {adnLibrary.getVersion()}");

        adnLibrary.setLogLevel(Antilatency.DeviceNetwork.LogLevel.Info);

        using var filter = adnLibrary.createFilter();
        filter.addUsbDevice(Antilatency.DeviceNetwork.Constants.AllUsbDevices);

        return adnLibrary.createNetwork(filter);
    }

    private static Antilatency.Alt.Environment.IEnvironment CreateEnvironment(
            Antilatency.StorageClient.ILibrary storageClientLibrary,
            Antilatency.Alt.Environment.Selector.ILibrary environmentSelectorLibrary) {

        using var storage = storageClientLibrary.getLocalStorage();
        string environmentCode = storage.read("environment", "default");

        if (string.IsNullOrEmpty(environmentCode)) {
            throw new Exception("Cannot create environment");
        }

        return environmentSelectorLibrary.createEnvironment(environmentCode);
    }

    private static Antilatency.Math.floatP3Q CreatePlacement(
            Antilatency.StorageClient.ILibrary storageClientLibrary,
            Antilatency.Alt.Tracking.ILibrary trackingLibrary) {

        using var storage = storageClientLibrary.getLocalStorage();
        string placementCode = storage.read("placement", "default");

        if (string.IsNullOrEmpty(placementCode)) {
            var identityPlacement = new Antilatency.Math.floatP3Q();
            identityPlacement.rotation.w = 1;

            Console.WriteLine("Failed to get placement code, using identity placement");
            return identityPlacement;
        }

        return trackingLibrary.createPlacement(placementCode);
    }

    private static Antilatency.DeviceNetwork.NodeHandle WaitForCompatibleNode(
            Antilatency.Alt.Tracking.ITrackingCotaskConstructor cotaskConstructor,
            Antilatency.DeviceNetwork.INetwork network) {

        uint prevUpdateId = 0;
        while (true) {
            uint updateId = network.getUpdateId();
            if (updateId == prevUpdateId) {
                Thread.Yield();
                continue;
            }

            Console.WriteLine($"Network ID changed: {prevUpdateId} -> {updateId}");

            var node = cotaskConstructor
                .findSupportedNodes(network)
                .FirstOrDefault(
                    n => network.nodeGetStatus(n) ==
                        Antilatency.DeviceNetwork.NodeStatus.Idle);

            if (node != Antilatency.DeviceNetwork.NodeHandle.Null) {
                return node;
            }

            prevUpdateId = updateId;
        }
    }

    private static void PrintTrackingState(
            Antilatency.Alt.Tracking.ITrackingCotask cotask,
            Antilatency.Math.floatP3Q placement) {

        while (!cotask.isTaskFinished()) {

            var state = cotask.getExtrapolatedState(placement, 0.06f);

            if (state.stability.stage ==
                        Antilatency.Alt.Tracking.Stage.InertialDataInitialization) {
                continue;
            }

            Console.WriteLine(
                "{0,-12} : {1,-11:G5} {2,-11:G5} {3,-11:G5} : " +
                "{4,-11:G4} {5,-11:G5} {6,-11:G5} {7:G5}",
                state.stability.stage,
                state.pose.position.x,
                state.pose.position.y,
                state.pose.position.z,
                state.pose.rotation.x,
                state.pose.rotation.y,
                state.pose.rotation.z,
                state.pose.rotation.w);

            // Do not print too often; 5 FPS is enough.
            Thread.Sleep(200);
        }
    }

    private static void PrintEnvironmentMarkers(
            Antilatency.Alt.Environment.IEnvironment environment) {

        var markers = environment.getMarkers();

        Console.WriteLine("Environment markers:");
        for (var i = 0; i < markers.Length; ++i) {
            Console.WriteLine("    marker {0,-15} : {1,-12:G5} {2,-12:G5} {3,-12:G5}",
                i, markers[i].x, markers[i].y, markers[i].z);
        }

        Console.WriteLine();
    }

    private static void PrintPlacementInfo(Antilatency.Math.floatP3Q placement) {
        Console.WriteLine("Placement:");
        Console.WriteLine("    offset: {0:G5} {1:G5} {2:G5}",
            placement.position.x,
            placement.position.y,
            placement.position.z);

        Console.WriteLine("    rotation: {0:G5} {1:G5} {2:G5} {3:G5}",
            placement.rotation.x,
            placement.rotation.y,
            placement.rotation.z,
            placement.rotation.w);

        Console.WriteLine();
    }
}
