using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using UnityEngine.InputSystem;
using System;
using TMPro;

public class Player : NetworkBehaviour
{
    public float moveSpeed = 5.0f;

    Vector3 forward = Vector3.forward;

    NetworkCharacterController cc;

    [SerializeField]
    Ball prefabBall;

    [SerializeField]
    PhysicBall prefabPhysicBall;

    [Networked]
    TickTimer delay { get; set; }

    [Networked]
    public bool spawnedProgectile { get; set; }

    /// <summary>
    /// Networked로 설정된 변수의 변화를 감지하는 클래스
    /// </summary>
    private ChangeDetector changeDetector;

    /// <summary>
    /// 플레이어 몸통 머티리얼
    /// </summary>
    public Material bodyMaterial;

    PlayerInputActions inputActions;

    TMP_Text messageText;

    private void Awake()
    {
        cc = GetComponent<NetworkCharacterController>();
        Transform child = transform.GetChild(0);
        bodyMaterial = child.GetComponent<Renderer>()?.material;

        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        inputActions.Player.Chat.performed += OnChat;
    }

    private void OnDisable()
    {
        inputActions.Player.Chat.performed -= OnChat;
        inputActions.Player.Disable();
    }

    /// <summary>
    /// 네트워크 틱별로 계속 실행되는 함수
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetworkInputData data)) // 서버쪽에서 입력 정보 받아오기
        {
            //data.direction.Normalize();                                   // 유닛 벡터(길이가 1, 주로 방향 저장하기 위해 사용)로 만들기

            cc.Move(Runner.DeltaTime * moveSpeed * data.direction);         // 초당 moveSpeed의 속도로 data.direction 방향으로 이동

            if (data.direction.sqrMagnitude > 0)
            {
                forward = data.direction;                                   // 회전 도중에 forward 방향으로 공이 발사되는 것을 방지
            }

            if (HasStateAuthority && delay.ExpiredOrNotRunning(Runner))     // 호스트인지 확인 && delay가 설정 안되었거나 0.5초 설정하고 만료되었는지 확인 ///// 서버인지 확인
            {
                if (data.buttons.IsSet(NetworkInputData.MouseButtonLeft))   // 마우스 왼쪽 버튼이 눌러져있다.
                {
                    delay = TickTimer.CreateFromSeconds(Runner, 0.5f);      // 발사 쿨타임 0.5초 지정
                    Runner.Spawn(prefabBall,                                // 생성할 프리팹
                                 transform.position + transform.forward,    // 생성할 위치 (자기 위치 + 입력 방향) ///// 플레이어의 한칸 앞에서 생성
                                 Quaternion.LookRotation(forward),          // 생성될 회전 (입력 방향쪽으로)
                                 Object.InputAuthority,                     // 생성한 플레이어의 입력 권한? (생성한 플레이어?)
                                 (runner, obj) =>                           // 스폰 직전에 실행되는 람다식
                                 {
                                     obj.GetComponent<Ball>().Init();
                                 });
                    spawnedProgectile = !spawnedProgectile;
                }
                if (data.buttons.IsSet(NetworkInputData.MouseButtonRight))
                {
                    delay = TickTimer.CreateFromSeconds(Runner, 0.5f);                  // 발사 쿨타임 0.5초 지정
                    Runner.Spawn(prefabPhysicBall,                                      // 생성할 프리팹
                                 transform.position + forward + Vector3.up * 0.5f,      // 생성할 위치 (자기 위치 + 입력 방향) ///// 플레이어의 한칸 앞에서 생성
                                 Quaternion.LookRotation(forward),                      // 생성될 회전 (입력 방향쪽으로)
                                 Object.InputAuthority,                                 // 생성한 플레이어의 입력 권한? (생성한 플레이어?)
                                 (runner, obj) =>                                       // 스폰 직전에 실행되는 람다식
                                 {
                                     obj.GetComponent<PhysicBall>().Init(moveSpeed * forward);
                                 });
                    spawnedProgectile = !spawnedProgectile;
                }
            }
        }
    }

    /// <summary>
    /// 스폰 된 이후에 실행되는 함수
    /// </summary>
    public override void Spawned()
    {
        changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState); ///// 실시간으로 받아옴..?
    }

    public override void Render()
    {
        // 이 네트워크 오브젝트에서 Networked로 설정된 변수에 변화가 있었던 것들을 모두 순회
        foreach (string change in changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(spawnedProgectile): // spawnedProgectile 변수가 변경되었을 때
                    bodyMaterial.color = Color.white;
                    break;
            }
        }

        // Render는 유니티 랜더 루프상에서 작동 => Update와 같은 간격
        bodyMaterial.color = Color.Lerp(bodyMaterial.color, Color.blue, Time.deltaTime * 2);
    }

    /// <summary>
    /// RPC 발동용 입력 처리 함수
    /// </summary>
    private void OnChat(InputAction.CallbackContext _)
    {
        if (Object.HasInputAuthority)       // 입력 권한이 있을 때 (자기 Player일 때)
        {
            RPC_SendMessage("Hello World"); // "Hello World"라고 호스트에게 보냄
        }
    }

    /// [설정 내용]
    /// 소스는 입력 권한이 있어야 한다. = 내 Player이어야 한다.
    /// 타겟은 상태 권한이 있어야 한다. = 타겟은 호스트다.
    /// 호스트 모드는 SourceIsHostPlayer = 플레이어 입장에서 RPC를 호출한다.
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SendMessage(string message, RpcInfo info = default)
    {
        RPC_RelayMessage(message, info.Source); // info.Source는 로컬 플레이어 (자기 자신의 PlayerRef)
    }

    /// [설정 내용]
    /// 소스는 상태 권한이 있어야 한다. = 소스가 호스트
    /// 타겟은 모두. = 받은 내용을 모두에게 전파하는 용도
    /// 호스트 모드는 SourceIsServer = 서버 입장에서 RPC를 보낸다.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All, HostMode = RpcHostMode.SourceIsServer)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource)
    {
        if (messageText == null)
            messageText = FindAnyObjectByType<TMP_Text>();

        if (messageSource == Runner.LocalPlayer)
        {
            // 서버가 내가 보낸 메시지를 나에게 보낸 경우 (내가 보낸 메시지를 받은 경우)
            message = $"You : {message}\n";
        }
        else
        {
            // 다른 사람이 보낸 메시지를 받은 경우
            message = $"Other : {message}\n";
        }
        messageText.text += message;
    }
}
