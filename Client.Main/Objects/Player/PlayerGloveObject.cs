﻿using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Client.Main.Objects.Player
{
    public class PlayerGloveObject : ModelObject
    {
        private PlayerClass _playerClass;
        public PlayerClass PlayerClass
        {
            get => _playerClass;
            set { _playerClass = value; }
        }

        private new ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<PlayerObject>();

        public async Task SetPlayerClassAsync(PlayerClass playerClass)
        {
            if (_playerClass != playerClass)
            {
                _playerClass = playerClass;
                await OnChangePlayerClass();
            }
        }

        public PlayerGloveObject()
        {
            RenderShadow = true;
        }

        private async Task OnChangePlayerClass()
        {
            Model = await BMDLoader.Instance.Prepare($"Player/GloveClass{(int)PlayerClass:D2}.bmd");
            if (Model != null && Status == GameControlStatus.Error)
                Status = GameControlStatus.Ready;
            else if (Model == null)
            {
                _logger?.LogDebug($"PlayerGloveObject: Failed to load model for PlayerClass {(int)PlayerClass}. Path: Player/GloveClass{(int)PlayerClass:D2}.bmd");
                Status = GameControlStatus.Error;
            }
        }
    }
}