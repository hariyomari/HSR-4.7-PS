# MUIP API

English | [简体中文](muip-api.zh-CN.md)

## 💡API Help

- External API call interfaces are supported starting from version 2.3.
- The main interface is the Dispatch interface with an entry point. For example, if your Dispatch is http://127.0.0.1:8080, the request parameters and responses are in JSON format.
- (1) Create Session Interface: http://127.0.0.1:8080/muip/create_session (supports POST)
  - -Optional parameter: key_type (type, only supports PEM or default XML)
  - -Response example:
  ```json
  {
    "code": 0,
    "message": "Created!",
    "data": {
        "rsaPublicKey": "***",
        "sessionId": "***",
        "expireTimeStamp": ***
    }
  }
  ```
- (2) Authorization Interface: http://127.0.0.1:8080/muip/auth_admin (supports POST)
  - -Required parameter 1: SessionId (obtained after requesting the Create Session Interface)
  - -Required parameter 2: admin_key (configured in config.json's MuipServer.AdminKey and encrypted under rsaPublicKey [obtained from Create Session Interface] using RSA [pacs#1])
  - -A wrong key deletes the session; create a new one before retrying. Wrong keys share the admin page login's per-client limit: after 8 failures within 15 minutes the client is refused with code 3 for 15 minutes.
  - -Response example:
  ```json
  {
    "code": 0,
    "message": "Authorized admin key successfully!",
    "data": {
        "sessionId": "***",
        "expireTimeStamp": ***
    }
  }
  ```
- (3) Command Submission Interface: http://127.0.0.1:8080/muip/exec_cmd (supports POST/GET)
  - -Required parameter 1: SessionId (obtained after requesting the Create Session Interface)
  - -Required parameter 2: Command (the command to be executed, encrypted under rsaPublicKey [obtained from Create Session Interface] using RSA [pacs#1])
  - -Required parameter 3: TargetUid (UID of the player executing the command)
  - -Response example:
    ```json
    {
      "code": 0,
      "message": "Success",
      "data": {
          "sessionId": "***",
          "message": "*** //after base64 encoding
      }
    }
    ```
- (4) Get Server Status Interface: http://127.0.0.1:8080/muip/server_information (supports POST/GET)
  - -Required parameter 1: SessionId (obtained after requesting the Create Session Interface)
  - -Response example:
   ```json
    {
      "code": 0,
      "message": "Success",
      "data": {
          "onlinePlayers": [
              {
                  "uid": 10001,
                  "name": "KEVIN",
                  "headIconId": 208001
              },
              ....
          ],
          "serverTime": 1720626191,
          "maxMemory": 16002.227,
          "usedMemory": 7938.5547,
         "programUsedMemory": 323
      }
    }
    ```
- (5) Get Player Information Interface: http://127.0.0.1:8080/muip/player_information (supports POST/GET)
  - -Required parameter 1: SessionId (obtained after requesting the Create Session Interface)
  - -Required parameter 2: Uid (player UID)
  - -Response example:
   ```json
    {
      "code": 0,
      "message": "Success",
      "data": {
          "uid": 10001,
          "name": "KEVIN",
          "signature": "",
          "headIconId": 208001,
          "curPlaneId": 10001,
          "curFloorId": 10001001,
          "playerStatus": "Explore",
          "stamina": 182,
          "recoveryStamina": 4,
          "assistAvatarList": Array[0],
          "displayAvatarList": Array[0],
          "finishedMainMissionIdList": Array[38],
          "finishedSubMissionIdList": Array[273],
          "acceptedMainMissionIdList": Array[67],
          "acceptedSubMissionIdList": Array[169]
      }
  }
  ```

## Session safety

Sessions expire after 15 minutes. Changing or disabling the admin key invalidates existing administrator sessions; clients must create and authenticate a new session. Rotating the key from the admin panel actively clears these sessions. Expired sessions are pruned every minute and before admission. The creation endpoint allows at most 1024 unexpired sessions globally and 32 per HTTP source address; overflow returns a nonzero `code`. Reuse a valid session rather than continuously creating new IDs.
