using ColossalFramework;
using UnityEngine;

namespace CSUtil.CameraControl {
	public class CameraController {
		public static CameraController Instance = new CameraController();

		public void GoToPos(Vector3 pos) {
			ToolsModifierControl.cameraController.ClearTarget();

			ToolsModifierControl.cameraController.m_targetPosition = pos;
		}

		public void ClearPos() {
			ToolsModifierControl.cameraController.SetOverrideModeOff();
		}

		public void GoToBuilding(ushort buildingId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.Building = buildingId;

			Vector3 pos = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId].m_position;

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToVehicle(ushort vehicleId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.Vehicle = vehicleId;

			Vector3 pos = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleId].GetLastFramePosition();

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToParkedVehicle(ushort parkedVehicleId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.ParkedVehicle = parkedVehicleId;

			Vector3 pos = Singleton<VehicleManager>.instance.m_parkedVehicles.m_buffer[parkedVehicleId].m_position;

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToSegment(ushort segmentId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.NetSegment = segmentId;

			Vector3 pos = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_bounds.center;

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToNode(ushort nodeId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.NetNode = nodeId;

			Vector3 pos = Singleton<NetManager>.instance.m_nodes.m_buffer[nodeId].m_position;

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToCitizenInstance(ushort citizenInstanceId, bool openInfoPanel = false) {
			InstanceID id = default(InstanceID);
			id.CitizenInstance = citizenInstanceId;

			Vector3 pos = Singleton<CitizenManager>.instance.m_instances.m_buffer[citizenInstanceId].GetLastFramePosition();

			GoToInstance(id, pos, openInfoPanel);
		}

		public void GoToInstance(InstanceID id, Vector3 pos, bool openInfoPanel = false) {
			pos.y = Camera.main.transform.position.y;

			ToolsModifierControl.cameraController.SetTarget(id, pos, true);
			Singleton<SimulationManager>.instance.m_ThreadingWrapper.QueueMainThread(() => {
				DefaultTool.OpenWorldInfoPanel(id, pos);
			});
		}
	}
}
