# Responsive Verification Checklist

Feature: `001-port-ui-mudblazor`

## Viewports
- Mobile baseline: 390x844 (portrait)
- Desktop baseline: 1440x900

## Surfaces To Validate

| Surface | Mobile | Desktop | Notes |
|---|---|---|---|
| Home (`Components/Pages/Home.razor`) | [ ] | [ ] | |
| Dashboard (`Components/Pages/Dashboard.razor`) | [ ] | [ ] | |
| Profile Settings (`Components/Pages/ProfileSettings.razor`) | [ ] | [ ] | |
| Game Board (`Components/Pages/GameBoard.razor`) | [ ] | [ ] | |
| Map Controls (`Components/Map/MapComponent.razor`) | [ ] | [ ] | |
| Player Board (`Components/Map/PlayerBoard.razor`) | [ ] | [ ] | |
| Main Layout/User Menu (`Components/Layout/*.razor`) | [ ] | [ ] | |
| Login (`Components/Account/Pages/Login.razor`) | [ ] | [ ] | |
| Register (`Components/Account/Pages/Register.razor`) | [ ] | [ ] | |
| Reset Password (`Components/Account/Pages/ResetPassword.razor`) | [ ] | [ ] | |

## Pass Criteria (Per Surface)
- [ ] No clipped or overlapping critical controls
- [ ] Primary actions remain reachable and readable
- [ ] Form inputs are usable without layout breakage
- [ ] Visual hierarchy remains clear
- [ ] No blocking interaction regressions compared to baseline behavior

## Validation Log
| Date | Surface | Mobile Result | Desktop Result | Validator | Notes |
|---|---|---|---|---|---|
