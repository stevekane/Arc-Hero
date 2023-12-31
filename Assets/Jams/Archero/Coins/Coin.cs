using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Archero {
  public class Coin : MonoBehaviour {
    [SerializeField] Rigidbody Rigidbody;
    [SerializeField] Collider CollectionTrigger;
    [SerializeField] Vector3 BurstForce = new Vector3(10, 1, 10);
    [SerializeField] float CollectSpeed = 40f;

    public static void SpawnCoins(Vector3 position, int amount) {
      for (int i = 0; i < amount; i++) {
        var c = CoinManager.Instance.CoinPool.Get();
        c.transform.SetPositionAndRotation(position, Quaternion.identity);
      }
    }

    void OnEnable() {
      var impulse = Vector3.Scale(BurstForce, Random.onUnitSphere) * Random.Range(.5f, 1f);
      impulse.y = Mathf.Abs(impulse.y);
      Rigidbody.AddForce(impulse, ForceMode.Impulse);
      Rigidbody.isKinematic = false;
      CollectionTrigger.enabled = false;
    }

    void OnDisable() {
      Rigidbody.isKinematic = false;
      CollectionTrigger.enabled = true;
    }

    public async Task Collect(TaskScope scope) {
      Rigidbody.isKinematic = true;
      CollectionTrigger.enabled = true;
      var player = Player.Instance;
      var accel = 60f;
      while (player && isActiveAndEnabled) {
        CollectSpeed += Time.fixedDeltaTime * accel;
        var delta = player.transform.position - transform.position;
        var dist = Mathf.Min(Time.fixedDeltaTime * CollectSpeed, delta.magnitude);
        transform.position += dist * delta.normalized;
        await scope.TickTime();
      }
    }

    void OnTriggerEnter(Collider other) {
      if (other.GetComponent<Player>() && other.TryGetComponent(out Upgrades us)) {
        us.CollectGold(1);
        CoinManager.Instance.CoinPool.Release(this);
      }
    }
  }
}