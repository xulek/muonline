using Client.Main.Objects.Player;

namespace Client.Main.Objects
{
    /// <summary>
    /// A character drawn with the player model and equipment parts (players and player-model NPCs).
    /// SourceMain applies equipment effects such as the full-set aura to every MODEL_PLAYER object.
    /// </summary>
    public interface IEquippedCharacter
    {
        PlayerHelmObject Helm { get; }
        PlayerArmorObject Armor { get; }
        PlayerPantObject Pants { get; }
        PlayerGloveObject Gloves { get; }
        PlayerBootObject Boots { get; }
        WeaponObject Weapon1 { get; }
        WeaponObject Weapon2 { get; }
    }
}
