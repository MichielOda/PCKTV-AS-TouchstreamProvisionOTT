/*
****************************************************************************
*  Copyright (c) 2023,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

dd/mm/2023	1.0.0.1		XXX, Skyline	Initial version
****************************************************************************
*/

namespace Script
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Newtonsoft.Json;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Common;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
	using Skyline.DataMiner.ExceptionHelper;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Messages.SLDataGateway;
	using Skyline.DataMiner.Net.Sections;
	using TouchstreamHelper;

	/// <summary>
	/// Represents a DataMiner Automation script.
	/// </summary>
	public class Script
	{
		private readonly int dsprovisionTable = 6400;
		private readonly int jsonRequestParameter = 20000;
		private DomHelper innerDomHelper;

		private enum ProvisionIndex
		{
			Result = 9,
			InstanceId = 11,
		}

		/// <summary>
		/// The script entry point.
		/// </summary>
		/// <param name="engine">Link with SLAutomation process.</param>
		public void Run(Engine engine)
		{
			var scriptName = "Build Provision Request";

			engine.GenerateInformation("START " + scriptName);
			var helper = new PaProfileLoadDomHelper(engine);
			innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

			var exceptionHelper = new ExceptionHelper(engine, innerDomHelper);
			var touchstream = Touchstream.GetDOMData(helper);

			try
			{
				if (!Touchstream.CheckStatus(touchstream.InstanceId, innerDomHelper, new[] { "in_progress" }, out string status))
				{
					helper.Log($"Activity not executed due to Instance status is not compatible to execute activity.", PaLogLevel.Error);
					helper.SendErrorMessageToTokenHandler();
					return;
				}

				IDms dms = engine.GetDms();
				IDmsElement element = dms.GetElement(touchstream.Element);

				TouchstreamRequest tsrequest = new TouchstreamRequest
				{
					Action = (int)ConfigurationAction.Provision,
					AssetId = touchstream.AssetId,
					BookingId = touchstream.InstanceId,
					ConfigurationType = (int)StreamType.Regular,
					EventId = touchstream.EventId,
					EventLabel = touchstream.Label,
					EventName = touchstream.EventName,
					RowId = null,
					TemplateName = touchstream.TemplateName,
					YoSpaceStreamIdHls = touchstream.YoSpaceHls,
					YoSpaceStreamIdMpd = touchstream.YoSpaceMpd,
					EventStartDate = touchstream.StartDate.ToOADate(),
					EventEndDate = DateTime.Now.ToOADate(),
					ReducedTemplate = Convert.ToBoolean(touchstream.ReducedTemplate),
					ForceUpdate = Convert.ToBoolean(touchstream.ForcedUpdate),
					DynamicGroup = String.IsNullOrWhiteSpace(touchstream.DynamicGroup) ? null : touchstream.DynamicGroup,
				};

				if (touchstream.MediaTailor.Count > 0)
				{
					tsrequest.Manifests = SetMediaTailorManifests(engine, touchstream.MediaTailor);
				}

				string sValue = JsonConvert.SerializeObject(tsrequest);
				element.GetStandaloneParameter<string>(jsonRequestParameter).SetValue(sValue);

				bool CheckTSEventProvisioned()
				{
					try
					{
						var provisionTable = element.GetTable(dsprovisionTable);
						var tableRows = provisionTable.GetRows();

						foreach (var row in tableRows)
						{
							if (Convert.ToString(row[(int)ProvisionIndex.InstanceId]).Equals(touchstream.InstanceId) &&
								(Convert.ToString(row[(int)ProvisionIndex.Result]).Equals("Completed") || Convert.ToString(row[(int)ProvisionIndex.Result]).Equals("Completed with Errors")))
							{
								return true;
							}
						}

						return false;
					}
					catch (Exception ex)
					{
						engine.Log("Exception thrown while checking completed TS event: " + ex);
						throw;
					}
				}

				if (Touchstream.Retry(CheckTSEventProvisioned, new TimeSpan(0, 5, 0)))
				{
					helper.Log($"TS Event {touchstream.EventName} provisioned.", PaLogLevel.Information);
					helper.TransitionState("inprogress_to_active");
					helper.SendFinishMessageToTokenHandler();
				}
				else
				{
					/*var log = new Log
					{
						AffectedItem = scriptName,
						AffectedService = touchstream.EventName,
						Timestamp = DateTime.Now,
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = touchstream.EventName,
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Severity = ErrorCode.SeverityType.Warning,
							Source = scriptName,
							Description = $"Failed to provision TS Event ({touchstream.EventName}) within the timeout time.",
						},
					};
					exceptionHelper.GenerateLog(log);*/
					helper.Log($"Failed to provision TS Event ({touchstream.EventName}) within the timeout time.", PaLogLevel.Error);
					helper.SendErrorMessageToTokenHandler();
				}
			}
			catch (Exception ex)
			{
				helper.Log($"Failed to provision TS Event ({touchstream.EventName}) due to exception: " + ex, PaLogLevel.Error);
				engine.GenerateInformation($"Failed to provision TS Event ({touchstream.EventName}) due to exception: " + ex);
				/*var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = touchstream.EventName,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = touchstream.EventName,
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Severity = ErrorCode.SeverityType.Major,
						Source = scriptName,
					},
				};
				exceptionHelper.ProcessException(ex, log);*/
				helper.SendErrorMessageToTokenHandler();
				throw;
			}
		}

		private List<MediaTailorManifest> SetMediaTailorManifests(Engine engine, List<Guid> mediaTailorInstances)
		{
			var manifestList = new List<MediaTailorManifest>();
			foreach (var mediaTailorInstanceId in mediaTailorInstances)
			{
				var mediaTailorInstanceFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(mediaTailorInstanceId));
				var mediaTailorInstance = innerDomHelper.DomInstances.Read(mediaTailorInstanceFilter).First();
				var mediaTailorSectionData = new Dictionary<string, string>();

				foreach (var section in mediaTailorInstance.Sections)
				{
					section.Stitch(SetSectionDefinitionById);

					foreach (var field in section.FieldValues)
					{
						mediaTailorSectionData[field.GetFieldDescriptor().Name] = field.Value.ToString();
					}
				}

				var resultUrl = mediaTailorSectionData["Result URL (MediaTailor)"];
				if (String.IsNullOrWhiteSpace(resultUrl))
				{
					continue;
				}

				manifestList.Add(new MediaTailorManifest
				{
					Url = resultUrl,
					Product = mediaTailorSectionData["Product (MediaTailor)"],
					Format = mediaTailorSectionData["Format (MediaTailor)"],
					Cdn = mediaTailorSectionData["CDN (MediaTailor)"],
				});
			}

			return manifestList;
		}

		private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
		{
			return innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
		}
	}
}