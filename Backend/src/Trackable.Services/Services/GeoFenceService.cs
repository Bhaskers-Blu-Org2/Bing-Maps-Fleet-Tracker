﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Trackable.Models;
using Trackable.Repositories;
using Trackable.Repositories.Helpers;

namespace Trackable.Services
{
    class GeoFenceService : CrudServiceBase<int, GeoFence, IGeoFenceRepository>, IGeoFenceService
    {
        private readonly INotificationService notificationService;
        private readonly IGeoFenceUpdateRepository geoFenceUpdateRepository;

        public GeoFenceService(
            IGeoFenceRepository repository,
            IGeoFenceUpdateRepository geoFenceUpdateRepository,
            INotificationService notificationService)
            : base(repository)
        {
            this.notificationService = notificationService;
            this.geoFenceUpdateRepository = geoFenceUpdateRepository;
        }

        public async override Task<IEnumerable<GeoFence>> AddAsync(IEnumerable<GeoFence> models)
        {
            var results = await base.AddAsync(models);

            var updatedList = new List<GeoFence>();
            foreach (var zipped in results.Zip(models, (r,m) => new { result = r, model = m }))
            {
                updatedList.Add(await this.repository.UpdateAssetsAsync(zipped.result, zipped.model.AssetIds));
            }
             
            return updatedList;
        }

        public async override Task<GeoFence> AddAsync(GeoFence model)
        {
            var result = await base.AddAsync(model);

            var updated = await this.repository.UpdateAssetsAsync(result, model.AssetIds);
            
            return updated;
        }

        public async override Task<IEnumerable<GeoFence>> UpdateAsync(IDictionary<int, GeoFence> models)
        {
            var updatedModels = await base.UpdateAsync(models);

            var updatedList = new List<GeoFence>();
            foreach (var updatedModel in updatedModels)
            {
                updatedList.Add(await this.repository.UpdateAssetsAsync(updatedModel, models[updatedModel.Id].AssetIds));
            }

            return updatedList;
        }

        public async override Task<GeoFence> UpdateAsync(int geoFenceId, GeoFence model)
        {
            var updatedModel = await base.UpdateAsync(geoFenceId, model);

            return await this.repository.UpdateAssetsAsync(updatedModel, model.AssetIds);
        }

        public async Task<IEnumerable<int>> HandlePoints(string assetId, params IPoint[] points)
        {
            var triggeredFences = new List<int>();
            var fences = await this.repository.GetByAssetIdAsync(assetId);
            var latestUpdates = await this.geoFenceUpdateRepository.GetLatestAsync(assetId);

            foreach (var fence in fences)
            {
                GeoFenceUpdate latestUpdate;
                var hasUpdate = latestUpdates.TryGetValue(fence.Id, out latestUpdate);

                // Continue if the cooldown period has yet to expire
                if (hasUpdate 
                    && latestUpdate.UpdatedAt + TimeSpan.FromMinutes(fence.Cooldown) > DateTime.UtcNow
                    && latestUpdate.NotificationStatus == NotificationStatus.Triggered)
                {
                    continue;
                }

                var fenceIsTriggered = points.Any(point => fence.IsTriggeredByPoint(point));
                var fenceStatus = fenceIsTriggered ? NotificationStatus.Triggered : NotificationStatus.NotTriggered;

                // Continue if there are no updates to the status of the asset
                if (hasUpdate && latestUpdate.NotificationStatus == fenceStatus)
                {
                    continue;
                }

                // Update the status of the asset
                await this.geoFenceUpdateRepository.AddAsync(new GeoFenceUpdate
                {
                    NotificationStatus = fenceStatus,
                    GeoFenceId = fence.Id,
                    AssetId = assetId
                });

                // Continue if the status has moved from triggered to untriggered 
                if (fenceStatus != NotificationStatus.Triggered)
                {
                    continue;
                }

                foreach (var email in fence.EmailsToNotify)
                {
                    await notificationService.NotifyViaEmail(
                        email,
                        $"{fence.Name} Geofence was triggered by asset {assetId}",
                        "",
                        $"<strong>{fence.FenceType.ToString()}</strong> Geofence <strong>{fence.Name}</strong> was triggered by asset  <strong>{assetId}</strong> at  <strong>{DateTime.UtcNow}</strong>");
                }

                triggeredFences.Add(fence.Id);
            }

            return triggeredFences;
        }
    }
}
