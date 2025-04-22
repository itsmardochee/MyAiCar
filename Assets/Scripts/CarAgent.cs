using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using System.Collections.Generic; // Assurez-vous que cette ligne est présente

public class CarAgent : Agent
{
    [Header("Movement")]
    public float moveSpeed = 15f;
    public float turnSpeed = 45f;

    [Header("Sensors")]
    public Transform raycastOrigin; // Point de départ des rayons (typiquement l'avant de la voiture)
    public LayerMask obstacleMask;  // Calques considérés comme obstacles par les rayons
    public float rayLength = 10f;   // Longueur des rayons

    [Header("Rewards")]
    public float checkpointReward = 0.5f;       // Récompense pour atteindre le bon checkpoint
    public float minDistanceToReachCheckpoint = 3f; // Utilisé principalement pour la récompense de progression, la détection du trigger est clé
    public float stationaryPenalty = 0.005f;    // Pénalité par frame si l'agent est bloqué
    public float progressRewardMultiplier = 0.01f; // Multiplicateur pour la récompense de rapprochement du checkpoint
    public float livingReward = 0.001f;         // Petite récompense à chaque pas de temps

    [Header("Checkpoints")]
    [Tooltip("Drag checkpoint Transforms into this list IN ORDER from start to finish.")]
    public List<Transform> checkpoints = new List<Transform>(); // Liste publique pour assigner dans l'Inspector

    [Header("Debugging")]
    public bool debugRays = true; // Activer/désactiver la visualisation des rayons
    public bool debugLogs = false; // Activer/désactiver les logs détaillés dans la console

    private Rigidbody rb;
    private Vector3 previousPosition; // Pour détecter si l'agent est bloqué
    private float previousCheckpointDistance; // Pour calculer la récompense de progrès
    private int stationaryFrames = 0;
    private const int StationaryThreshold = 30; // Nombre de frames consécutives immobiles avant pénalité

    private int currentCheckpointIndex = 0; // Index du prochain checkpoint à atteindre
    private List<bool> checkpointsReached = new List<bool>(); // Pour suivre quels checkpoints ont été atteints

    // Position et rotation de départ fixes (comme demandé)
    private Vector3 startPosition = new Vector3(954.39f, 8.27f, 791.74f);
    private Quaternion startRotation = Quaternion.Euler(0f, 180f, 0f);

    public override void Initialize()
    {
        // Cette méthode est appelée une seule fois au début
        rb = GetComponent<Rigidbody>();

        // Vérification initiale : assurez-vous que la liste de checkpoints n'est pas vide.
        // Ils doivent être assignés via l'Inspector.
        if (checkpoints == null || checkpoints.Count == 0)
        {
            Debug.LogError("Checkpoint list is empty or null! Assign checkpoint Transforms in the Inspector.");
            // Vous pourriez vouloir désactiver l'agent ou l'académie ici si c'est critique
            // gameObject.SetActive(false);
            // Academy.Instance.gameObject.SetActive(false);
        }

        // Initialiser la liste de suivi des checkpoints atteints
        checkpointsReached.Clear();
        for (int i = 0; i < checkpoints.Count; i++)
        {
            checkpointsReached.Add(false);
        }
        if (debugLogs) Debug.Log($"Agent Initialized with {checkpoints.Count} checkpoints.");
    }

    public override void OnEpisodeBegin()
    {
        // Réinitialiser la voiture à sa position et rotation de départ
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = startPosition;
        transform.rotation = startRotation;

        // Réinitialiser la progression des checkpoints
        ResetCheckpointColors(); // Remettre les couleurs des checkpoints
        currentCheckpointIndex = 0;
        for (int i = 0; i < checkpointsReached.Count; i++)
        {
            checkpointsReached[i] = false;
        }

        // Réinitialiser le suivi de l'immobilité et de la progression
        previousPosition = transform.position;
        stationaryFrames = 0;

        // Initialiser la distance de référence au premier checkpoint (si existant)
        if (checkpoints.Count > 0)
        {
            previousCheckpointDistance = Vector3.Distance(transform.position, checkpoints[0].position);
        }
        else
        {
            previousCheckpointDistance = 0; // Cas sans checkpoints (ne devrait pas arriver si Initialize vérifie)
        }

        if (debugLogs) Debug.Log("Episode Begin. Resetting agent.");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // --- Observations des Raycasts (pour l'évitement) ---
        Vector3[] rayDirections = {
            transform.forward, // Devant
            Quaternion.Euler(0, 30, 0) * transform.forward,  // Devant +30°
            Quaternion.Euler(0, -30, 0) * transform.forward, // Devant -30°
            Quaternion.Euler(0, 60, 0) * transform.forward,  // Devant +60°
            Quaternion.Euler(0, -60, 0) * transform.forward  // Devant -60°
        };

        foreach (var dir in rayDirections)
        {
            RaycastHit hit;
            // Effectuer le raycast. obstacleMask assure qu'il ne détecte que les calques spécifiés.
            if (Physics.Raycast(raycastOrigin.position, dir, out hit, rayLength, obstacleMask))
            {
                // Ajouter la distance normalisée à l'obstacle
                sensor.AddObservation(hit.distance / rayLength);
                // Visualiser le rayon en rouge s'il touche quelque chose
                if (debugRays) Debug.DrawRay(raycastOrigin.position, dir * hit.distance, Color.red, 0.1f);
            }
            else
            {
                // Ajouter 1 si aucun obstacle n'est touché dans la portée du rayon
                sensor.AddObservation(1f);
                // Visualiser le rayon en vert s'il ne touche rien
                if (debugRays) Debug.DrawRay(raycastOrigin.position, dir * rayLength, Color.green, 0.1f);
            }
        }

        // --- Observations pour la navigation (vers le prochain checkpoint) ---
        if (currentCheckpointIndex < checkpoints.Count)
        {
            Transform nextCheckpoint = checkpoints[currentCheckpointIndex];
            // Calculer le vecteur directionnel vers le checkpoint
            Vector3 dirToCheckpoint = (nextCheckpoint.position - transform.position);
            float distToCheckpoint = dirToCheckpoint.magnitude; // Distance réelle

            // Normaliser le vecteur directionnel pour avoir seulement la direction
            dirToCheckpoint.Normalize();

            // Calculer l'angle entre le vecteur 'avant' de la voiture et le vecteur directionnel vers le checkpoint
            float angle = Vector3.SignedAngle(transform.forward, dirToCheckpoint, Vector3.up);
            // Normaliser l'angle entre -1 et 1 (-180° à 180° -> -1 à 1)
            angle /= 180f;

            sensor.AddObservation(dirToCheckpoint.x); // Composante X de la direction normalisée
            sensor.AddObservation(dirToCheckpoint.z); // Composante Z de la direction normalisée (on ignore Y car la navigation est 2D au sol)
            sensor.AddObservation(angle);             // Angle normalisé
            // Normalisation de la distance. 50f est une valeur de référence.
            // Ajustez cette valeur si la distance maximale typicale dans votre scène est très différente.
            // Une petite distance donnera une observation proche de 0, une grande distance proche de 1 (ou plus si dist > 50).
            sensor.AddObservation(distToCheckpoint / 50f);

            if (debugLogs) Debug.DrawLine(transform.position, nextCheckpoint.position, Color.yellow, 0.1f); // Visualiser la ligne vers le checkpoint
        }
        else
        {
            // Si tous les checkpoints ont été atteints, fournir des observations neutres pour la navigation
            sensor.AddObservation(0f); // Dir X
            sensor.AddObservation(0f); // Dir Z
            sensor.AddObservation(0f); // Angle
            sensor.AddObservation(0f); // Distance
            if (debugLogs && checkpoints.Count > 0) Debug.DrawLine(transform.position, checkpoints[checkpoints.Count - 1].position, Color.gray, 0.1f); // Visualiser la ligne vers le dernier CP atteint
        }

        // --- Observation de la vitesse ---
        // Ajouter la vitesse actuelle normalisée par la vitesse maximale possible
        sensor.AddObservation(rb.linearVelocity.magnitude / moveSpeed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Appliquer les actions de l'agent ---
        float moveInput = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f); // Action de déplacement (-1: arrière, 1: avant)
        float turnInput = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f); // Action de rotation (-1: gauche, 1: droite)

        // Appliquer le mouvement et la rotation via le Rigidbody
        // Utiliser Time.fixedDeltaTime car OnActionReceived est appelé dans FixedUpdate par défaut.
        rb.MovePosition(transform.position + transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turnInput * turnSpeed * Time.fixedDeltaTime, 0f));

        // --- Calcul des récompenses ---

        // Récompense de vie : encourage l'agent à rester en vie
        AddReward(livingReward);

        // Récompense de progrès vers le prochain checkpoint
        // Cette récompense encourage l'agent à se rapprocher *continuellement* du checkpoint cible.
        if (currentCheckpointIndex < checkpoints.Count)
        {
            float currentDistance = Vector3.Distance(transform.position, checkpoints[currentCheckpointIndex].position);
            float delta = previousCheckpointDistance - currentDistance; // Calcule si l'agent s'est rapproché (delta > 0) ou éloigné (delta < 0)
            AddReward(delta * progressRewardMultiplier); // Ajoute une récompense proportionnelle au rapprochement
            previousCheckpointDistance = currentDistance; // Met à jour la distance de référence pour le prochain pas

            // if (debugLogs) Debug.Log($"Step: Target CP: {currentCheckpointIndex}, Dist: {currentDistance:F2}, Delta: {delta:F2}, Step Reward: {delta * progressRewardMultiplier:F4}");
        }


        // Pénalité si la voiture est bloquée (ne bouge pas assez)
        float distanceMoved = Vector3.Distance(transform.position, previousPosition);
        if (distanceMoved < 0.01f) // Seuil de distance très faible pour être considéré comme immobile
        {
            stationaryFrames++;
            if (stationaryFrames > StationaryThreshold)
            {
                if (debugLogs) Debug.Log($"Agent stuck! Frames: {stationaryFrames}");
                AddReward(-stationaryPenalty); // Applique une petite pénalité
                // Optionnel : EndEpisode(); ici si vous voulez terminer l'épisode si l'agent est bloqué trop longtemps
            }
        }
        else
        {
            stationaryFrames = 0; // Réinitialise le compteur si l'agent bouge
        }

        // Mettre à jour la position précédente pour le calcul de l'immobilité
        previousPosition = transform.position;

        // Le passage de checkpoint est géré par OnTriggerEnter pour être plus précis et fiable.
    }

    // Cette méthode est appelée lorsque le Rigidbody de l'agent entre en collision avec un autre collider
    private void OnCollisionEnter(Collision collision)
    {
        // Pénalité pour collision avec des murs ou des obstacles, ou chute hors de la piste
        if (collision.gameObject.CompareTag("Wall"))
        {
            if (debugLogs) Debug.Log($"Collision with Wall. Ending episode. Reward: {GetCumulativeReward():F2}");
            AddReward(-1f); // Grosse pénalité
            EndEpisode();   // Terminer l'épisode immédiatement
        }
        else if (transform.localPosition.y < -20)
        {
            if (debugLogs) Debug.Log($"Agent fell off track. Ending episode. Reward: {GetCumulativeReward():F2}");
            AddReward(-1f); // Grosse pénalité
            EndEpisode();   // Terminer l'épisode immédiatement
        }
        else if (collision.gameObject.CompareTag("Cube")) // Exemple d'autre type d'obstacle
        {
            if (debugLogs) Debug.Log($"Collision with Cube. Ending episode. Reward: {GetCumulativeReward():F2}");
            AddReward(-0.5f); // Pénalité moyenne
            EndEpisode();
        }
        // Le tag "Finish" est généralement géré par le dernier checkpoint, mais si vous avez un collider distinct :
        /*
        else if (collision.gameObject.CompareTag("Finish"))
        {
            if (debugLogs) Debug.Log($"Collision with Finish line. Ending episode. Reward: {GetCumulativeReward():F2}");
            AddReward(1f); // Récompense pour avoir atteint la fin (peut être redondant avec le dernier CP)
            EndEpisode();
        }
        */
    }

    // Cette méthode est appelée lorsque le collider de l'agent (configuré en Trigger)
    // entre dans un autre collider configuré en Trigger.
    private void OnTriggerEnter(Collider other)
    {
        // Vérifier si le trigger touché est un checkpoint
        if (other.CompareTag("Checkpoint"))
        {
            // Parcourir la liste des checkpoints pour trouver lequel a été touché
            for (int i = 0; i < checkpoints.Count; i++)
            {
                // S'assurer que le trigger touché correspond au checkpoint dans notre liste
                if (other.transform == checkpoints[i])
                {
                    // Si c'est le checkpoint actuel qu'on n'a pas encore atteint
                    if (i == currentCheckpointIndex && !checkpointsReached[i])
                    {
                        // C'est le bon checkpoint suivant !
                        if (debugLogs) Debug.Log($"Passed Checkpoint {i}. Giving reward {checkpointReward}.");
                        AddReward(checkpointReward);       // Ajouter la récompense pour le passage
                        checkpointsReached[i] = true;      // Marquer ce checkpoint comme atteint
                        currentCheckpointIndex++;          // Passer au checkpoint suivant

                        // Mettre à jour la distance de référence pour la récompense de progrès
                        if (currentCheckpointIndex < checkpoints.Count)
                        {
                            previousCheckpointDistance = Vector3.Distance(transform.position, checkpoints[currentCheckpointIndex].position);
                            if (debugLogs) Debug.Log($"Next target checkpoint index: {currentCheckpointIndex}");
                            // Changer la couleur des checkpoints
                            ChangeCheckpointColor(checkpoints[i], Color.grey);
                            ChangeCheckpointColor(checkpoints[currentCheckpointIndex], Color.yellow);
                        }

                        // Vérifier si tous les checkpoints ont été atteints
                        if (currentCheckpointIndex >= checkpoints.Count)
                        {
                            if (debugLogs) Debug.Log($"All checkpoints reached! Bonus reward {1.0f}. Total: {GetCumulativeReward():F2}");
                            AddReward(1.0f);
                            EndEpisode();
                        }
                    }
                    // Ne pas pénaliser pour les checkpoints déjà atteints
                    else if (i < currentCheckpointIndex)
                    {
                        // C'est un checkpoint déjà franchi, ne rien faire
                        if (debugLogs) Debug.Log($"Re-entered already passed checkpoint {i}, ignoring.");
                    }
                    // Uniquement pénaliser pour les futurs checkpoints hors séquence
                    else if (i > currentCheckpointIndex)
                    {
                        if (debugLogs) Debug.Log($"Wrong checkpoint order! Touched {i}, expecting {currentCheckpointIndex}. Penalty -0.5.");
                        AddReward(-0.5f);
                    }

                    // Dans tous les cas, on a trouvé le checkpoint correspondant, sortir de la boucle
                    break;
                }
            }
        }
    }
    
    // Méthode pour le contrôle manuel via clavier (pour le test et le débogage)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        // Mapping des inputs clavier aux actions continues de l'agent
        // Vertical (W/S ou Flèches Haut/Bas) -> Mouvement avant/arrière
        // Horizontal (A/D ou Flèches Gauche/Droite) -> Rotation gauche/droite
        continuousActions[0] = Input.GetAxis("Vertical");
        continuousActions[1] = Input.GetAxis("Horizontal");
    }

    // --- Fonctions d'aide à la visualisation (facultatives) ---
    // Si vos objets checkpoint ont un Renderer (MeshRenderer, etc.), vous pouvez utiliser
    // ces fonctions pour changer leur apparence en fonction de l'état.

    private void ChangeCheckpointColor(Transform checkpointTransform, Color color)
    {
        Renderer renderer = checkpointTransform.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Créer une nouvelle instance du matériau pour éviter de modifier le matériau partagé
            if (renderer.material.color != color) // Évite de créer un nouveau matériau inutilement
            {
                renderer.material = new Material(renderer.material);
                renderer.material.color = color;
            }
        }
    }

    private void ResetCheckpointColors()
    {
        // Remet la couleur de base des checkpoints (par exemple, blanc ou vert initial)
        // Appeler ceci au début de l'épisode
        foreach (var cp in checkpoints)
        {
            Renderer renderer = cp.GetComponent<Renderer>();
            if (renderer != null && renderer.material.color != Color.white) // Remplacez Color.white par votre couleur de base
            {
                renderer.material = new Material(renderer.material);
                renderer.material.color = Color.white; // Votre couleur de base ici
            }
        }
        // Mettre la couleur du premier checkpoint en jaune au début si la liste n'est pas vide
        if (checkpoints.Count > 0)
        {
            ChangeCheckpointColor(checkpoints[0], Color.yellow);
        }
    }

}
