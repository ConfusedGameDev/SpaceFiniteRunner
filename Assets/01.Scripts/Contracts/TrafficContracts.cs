namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>
    /// What oncoming traffic needs to know about a body it can run into: where
    /// it is in track space, how big it is and whether it can be hit at all.
    /// Contact is analytic and SWEPT (the traffic system keeps the last tick's
    /// distance itself), never a collider. Distances are metres from the track
    /// start.
    /// </summary>
    public interface ITrafficBody
    {
        /// <summary>Track distance of the simulation (the physics tick's value).</summary>
        float Distance { get; }
        /// <summary>Lateral offset from the flight line, metres.</summary>
        float Lateral { get; }
        /// <summary>Height of its root above the flight line, metres (a ship on a jump clears a car).</summary>
        float TrafficHeight { get; }
        /// <summary>Half size for contact: x across the track, y up, z along it.</summary>
        UnityEngine.Vector3 TrafficReach { get; }
        /// <summary>False while it cannot touch traffic at all (off the track, hidden, gone, mid-exchange).</summary>
        bool TrafficSolid { get; }
    }

    /// <summary>A body traffic destroys on contact (the patrol): the car explodes, and so does it.</summary>
    public interface ITrafficVictim : ITrafficBody
    {
        void HitByTraffic();
    }
}
