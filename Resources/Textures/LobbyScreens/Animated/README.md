# Animated Lobby Backgrounds

Place `.gif` files in this folder and reference them from `Resources/Prototypes/lobbyscreens.yml` via `backgroundGif`.

Example:

```yml
- type: lobbyBackground
  id: AnimatedExample
  backgroundGif: /Textures/LobbyScreens/Animated/example.gif
```

Client runtime decodes GIF frames and per-frame delays, then plays them as lobby background animation.
